using System.Text.Json;
using Dapper;
using FinGuitar.Web.Data;
using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;

namespace FinGuitar.Web.Chat;

/// <summary>What a confirm, final confirm or cancel did: the card's new state plus any messages the server added.</summary>
public record ActionOutcome(ProposedAction Action, IReadOnlyList<ChatMessage> NewMessages, string? Notice = null);

/// <summary>
/// Creates proposals and applies them only after the user clicks (FR-C8 to FR-C10, FR-C16, FR-C17).
/// Applying calls the same service methods the admin pages call, with the origin marked through ChangeContext.
/// </summary>
public class ProposedActionService(
    Db db, ChatStore store, CurrentUser user, ChangeContext context, OrderService orders, ProductService products)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string Columns = @"id, user_id, conversation_id, message_id, request_message_id, action_type, arguments_json, summary,
        is_critical, critical_warning, status, created_at, first_confirmed_at, resolved_at, result_text";

    public async Task<ProposedAction> ProposeAsync(int conversationId, int requestMessageId, ProposedActionType type, object args, string summary)
    {
        var verdict = args switch
        {
            OrderStatusArgs o => CriticalActionPolicy.Evaluate(o),
            ProductUpdateArgs p => CriticalActionPolicy.Evaluate(p),
            _ => throw new ArgumentException("Unknown proposal arguments", nameof(args)),
        };

        var action = new ProposedAction
        {
            Id = Guid.NewGuid(),
            UserId = user.UserId,
            ConversationId = conversationId,
            RequestMessageId = requestMessageId,
            ActionType = type,
            ArgumentsJson = JsonSerializer.Serialize(args, args.GetType(), Json),
            Summary = summary,
            IsCritical = verdict.IsCritical,
            CriticalWarning = verdict.Warning,
            Status = ProposedActionStatus.Pending,
            CreatedAt = store.Now,
        };

        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await ExpireForConversationAsync(conn, tx, conversationId);
        await conn.ExecuteAsync(
            @"INSERT INTO proposed_actions (id, user_id, conversation_id, request_message_id, action_type, arguments_json, summary,
                    is_critical, critical_warning, status, created_at)
              VALUES (@Id, @UserId, @ConversationId, @RequestMessageId, @type, @ArgumentsJson, @Summary,
                    @critical, @CriticalWarning, 'Pending', @CreatedAt)",
            new
            {
                action.Id, action.UserId, action.ConversationId, action.RequestMessageId, type = type.ToString(),
                action.ArgumentsJson, action.Summary, critical = action.IsCritical ? 1 : 0, action.CriticalWarning, action.CreatedAt,
            }, tx);
        await tx.CommitAsync();
        return action;
    }

    /// <summary>Called inside the start-of-turn transaction. Expiry is per conversation (D2).</summary>
    public async Task ExpireForConversationAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction? tx, int conversationId) =>
        await conn.ExecuteAsync(
            @"UPDATE proposed_actions SET status = 'Expired', resolved_at = @now
              WHERE conversation_id = @conversationId AND status IN ('Pending', 'AwaitingFinalConfirm')",
            new { conversationId, now = store.Now }, tx);

    public async Task LinkMessageAsync(Guid actionId, int messageId)
    {
        await using var conn = await db.OpenAsync();
        await conn.ExecuteAsync("UPDATE proposed_actions SET message_id = @messageId WHERE id = @actionId", new { actionId, messageId });
    }

    /// <summary>For rendering: the action only if it belongs to the signed-in user.</summary>
    public async Task<ProposedAction?> GetForViewAsync(Guid id)
    {
        await using var conn = await db.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<ProposedAction>(
            $"SELECT {Columns} FROM proposed_actions WHERE id = @id AND user_id = @userId", new { id, userId = user.UserId });
    }

    public Task<ActionOutcome> ConfirmAsync(Guid id) => ConfirmCoreAsync(id, finalStep: false);

    public Task<ActionOutcome> FinalConfirmAsync(Guid id) => ConfirmCoreAsync(id, finalStep: true);

    private async Task<ActionOutcome> ConfirmCoreAsync(Guid id, bool finalStep)
    {
        var action = await GetForViewAsync(id) ?? throw new NotFoundException("That card does not exist.");
        user.RequireLevel1();

        var expected = finalStep ? ProposedActionStatus.AwaitingFinalConfirm : ProposedActionStatus.Pending;
        if (action.Status != expected)
            return new ActionOutcome(action, [], StateNotice(action));

        if (action.EffectiveStatus(store.Now) == ProposedActionStatus.Expired)
        {
            await SetStatusAsync(id, expected, ProposedActionStatus.Expired);
            return new ActionOutcome(await Reload(id), [], "Expired. Ask again.");
        }

        if (!finalStep && action.IsCritical)
            return await AskFinalConfirmationAsync(action);

        return await ApplyAsync(action, expected, confirmations: finalStep ? 2 : 1);
    }

    /// <summary>The first confirm of a critical action changes no data; the server posts the final card (FR-C16).</summary>
    private async Task<ActionOutcome> AskFinalConfirmationAsync(ProposedAction action)
    {
        var verdict = EvaluateStored(action);
        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var moved = await conn.ExecuteAsync(
            "UPDATE proposed_actions SET status = 'AwaitingFinalConfirm', first_confirmed_at = @now WHERE id = @id AND status = 'Pending'",
            new { id = action.Id, now = store.Now }, tx);
        if (moved == 0) return new ActionOutcome(action, [], "This card was already used.");

        var payload = new FinalConfirmCardPayload(action.Id, action.Summary, verdict.Warning ?? "", verdict.DangerLabel ?? "Yes, do it", verdict.KeepLabel ?? "Keep as is");
        var messageId = await store.AddMessageAsync(conn, tx, action.ConversationId, MessageRole.Assistant,
            "Please confirm once more. This is a critical change.", PayloadKinds.FinalConfirmCard, ChatPayloadJson.Serialize(payload));
        await tx.CommitAsync();

        return new ActionOutcome(await Reload(action.Id), [await store.GetMessageAsync(conn, messageId)]);
    }

    private async Task<ActionOutcome> ApplyAsync(ProposedAction action, ProposedActionStatus expected, int confirmations)
    {
        // Claim it: only one caller can move it to Applying, so a double click cannot run it twice.
        await using (var conn = await db.OpenAsync())
        {
            var claimed = await conn.ExecuteAsync(
                "UPDATE proposed_actions SET status = 'Applying' WHERE id = @id AND status = @expected",
                new { id = action.Id, expected = expected.ToString() });
            if (claimed == 0) return new ActionOutcome(await Reload(action.Id), [], "This card was already used.");
        }

        string result;
        var status = ProposedActionStatus.Confirmed;
        try
        {
            result = await RunAsync(action, confirmations);
        }
        catch (Exception ex) when (ex is DomainException or ValidationException)
        {
            status = ProposedActionStatus.Failed;
            result = ex is ValidationException v ? string.Join(" ", v.Errors.Values) : ex.Message;
        }

        await using var conn2 = await db.OpenAsync();
        await using var tx = await conn2.BeginTransactionAsync();
        await conn2.ExecuteAsync(
            "UPDATE proposed_actions SET status = @status, resolved_at = @now, result_text = @result WHERE id = @id",
            new { id = action.Id, status = status.ToString(), now = store.Now, result }, tx);
        var text = status == ProposedActionStatus.Confirmed ? result : $"Not changed. {result}";
        var messageId = await store.AddMessageAsync(conn2, tx, action.ConversationId, MessageRole.ActionResult, text, isError: status == ProposedActionStatus.Failed);
        await tx.CommitAsync();

        return new ActionOutcome(await Reload(action.Id), [await store.GetMessageAsync(conn2, messageId)]);
    }

    /// <summary>Calls the same service method the admin page uses; the service runs its own transaction, rules and audit entry (2.4.1).</summary>
    private async Task<string> RunAsync(ProposedAction action, int confirmations)
    {
        string requestText;
        await using (var conn = await db.OpenAsync())
            requestText = await conn.ExecuteScalarAsync<string>("SELECT text FROM chat_messages WHERE id = @id", new { id = action.RequestMessageId }) ?? "";

        switch (action.ActionType)
        {
            case ProposedActionType.OrderStatusChange:
            {
                var args = JsonSerializer.Deserialize<OrderStatusArgs>(action.ArgumentsJson, Json)!;
                context.MarkAsChat(action.ConversationId, action.RequestMessageId, action.Id.ToString(), requestText, confirmations);
                await orders.ChangeStatusAsync(args.OrderId, args.To);
                return $"Order {args.OrderId} is now {args.To}.";
            }
            default:
            {
                var args = JsonSerializer.Deserialize<ProductUpdateArgs>(action.ArgumentsJson, Json)!;
                var current = await products.GetAsync(args.ProductId) ?? throw new NotFoundException($"Product {args.ProductId} not found.");
                if (current.Price != args.OldPrice || current.Stock != args.OldStock)
                    throw new DomainException($"Product {args.ProductId} changed since this was proposed. Ask again.");

                context.MarkAsChat(action.ConversationId, action.RequestMessageId, action.Id.ToString(), requestText, confirmations);
                await products.UpdateAsync(args.ProductId, args.NewPrice, args.NewStock);
                return $"Product {args.ProductId} {args.Name} updated: {action.Summary.Split(": ", 2).Last()}.";
            }
        }
    }

    /// <summary>Cancel, or "Keep …" on the final card. Owner only, while the card is still open.</summary>
    public async Task<ActionOutcome> CancelAsync(Guid id)
    {
        var action = await GetForViewAsync(id) ?? throw new NotFoundException("That card does not exist.");
        if (action.Status is not (ProposedActionStatus.Pending or ProposedActionStatus.AwaitingFinalConfirm))
            return new ActionOutcome(action, [], StateNotice(action));

        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var changed = await conn.ExecuteAsync(
            @"UPDATE proposed_actions SET status = 'Cancelled', resolved_at = @now, result_text = 'Cancelled. Nothing was changed.'
              WHERE id = @id AND status IN ('Pending', 'AwaitingFinalConfirm')", new { id, now = store.Now }, tx);
        if (changed == 0) return new ActionOutcome(await Reload(id), [], "This card was already used.");

        var messageId = await store.AddMessageAsync(conn, tx, action.ConversationId, MessageRole.ActionResult, "Cancelled. Nothing was changed.");
        await tx.CommitAsync();
        return new ActionOutcome(await Reload(id), [await store.GetMessageAsync(conn, messageId)]);
    }

    public CriticalVerdict EvaluateStored(ProposedAction action) => action.ActionType == ProposedActionType.OrderStatusChange
        ? CriticalActionPolicy.Evaluate(JsonSerializer.Deserialize<OrderStatusArgs>(action.ArgumentsJson, Json)!)
        : CriticalActionPolicy.Evaluate(JsonSerializer.Deserialize<ProductUpdateArgs>(action.ArgumentsJson, Json)!);

    private async Task SetStatusAsync(Guid id, ProposedActionStatus from, ProposedActionStatus to)
    {
        await using var conn = await db.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE proposed_actions SET status = @to, resolved_at = @now WHERE id = @id AND status = @from",
            new { id, from = from.ToString(), to = to.ToString(), now = store.Now });
    }

    private async Task<ProposedAction> Reload(Guid id) => (await GetForViewAsync(id))!;

    private string StateNotice(ProposedAction a) => a.EffectiveStatus(store.Now) switch
    {
        ProposedActionStatus.Expired => "Expired. Ask again.",
        ProposedActionStatus.Pending when a.IsCritical => "Use the buttons on the card.",
        _ => "This card was already used.",
    };
}
