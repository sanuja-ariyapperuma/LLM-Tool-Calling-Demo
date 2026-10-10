using System.Net;
using FinGuitar.Web.Data;
using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using AiMessage = Microsoft.Extensions.AI.ChatMessage;

namespace FinGuitar.Web.Chat;

public record SendResult(ChatConversation Conversation, IReadOnlyList<ChatMessage> NewMessages);

/// <summary>One chat turn (2.6.3): validate, save the user message, ask the model with this role's tools, save the reply.</summary>
public class ChatService(
    Db db, ChatStore store, ProposedActionService actions, ChatToolCatalog catalog, ToolResultLog log, CurrentUser user,
    IChatClient client, IOptions<LlmOptions> options, ILogger<ChatService> logger)
{
    public const int MaxMessageLength = 1000;
    public const int HistoryMessages = 20;
    public const int MaxToolRounds = 5;

    public const string Unavailable = "The assistant is not available right now. Check that the language model service is running and configured.";
    public const string ModelDownloading = "The assistant is still downloading its model. Try again in a few minutes.";
    public const string RoundLimit = "I couldn't finish that. Try asking more specifically.";
    public const string SomethingWrong = "Something went wrong. Please try again.";

    public async Task<SendResult> SendAsync(int? conversationId, string? text)
    {
        text = text?.Trim() ?? "";
        if (text.Length == 0)
            throw new ValidationException(new Dictionary<string, string> { ["Text"] = "Type a message first." });
        if (text.Length > MaxMessageLength)
            throw new ValidationException(new Dictionary<string, string> { ["Text"] = $"Messages are limited to {MaxMessageLength.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} characters." });

        // Transaction 1: the conversation, expiring this conversation's open cards (D2), and the user's message.
        int convId, requestId;
        await using (var conn = await db.OpenAsync())
        await using (var tx = await conn.BeginTransactionAsync())
        {
            if (conversationId is { } existing)
            {
                if (await store.GetConversationAsync(existing, user.UserId) is null)
                    throw new NotFoundException("That conversation does not exist.");
                convId = existing;
            }
            else
            {
                convId = await store.CreateConversationAsync(conn, tx, user.UserId, Truncate(text, 60));
            }
            await actions.ExpireForConversationAsync(conn, tx, convId);
            requestId = await store.AddMessageAsync(conn, tx, convId, MessageRole.User, text);
            await tx.CommitAsync();
        }

        log.ConversationId = convId;
        log.RequestMessageId = requestId;

        var (replyText, isError) = await AskModelAsync(convId);

        var payload = log.Choose();
        replyText = FlagFalseConfirmation(replyText);
        if (string.IsNullOrWhiteSpace(replyText))
            replyText = payload is null ? "I'm not sure how to help with that. Try asking about orders, products, customers or sales." : payload.Kind == PayloadKinds.ProposalCard ? "Please review the change below and confirm it." : "Here is what I found.";

        // Transaction 2: the assistant message with its payload, and the proposal's link to it.
        ChatMessage assistant, userMessage;
        await using (var conn = await db.OpenAsync())
        await using (var tx = await conn.BeginTransactionAsync())
        {
            var assistantId = await store.AddMessageAsync(conn, tx, convId, MessageRole.Assistant, replyText,
                payload?.Kind, payload is null ? null : ChatPayloadJson.Serialize(payload.Data), isError);
            await tx.CommitAsync();
            assistant = await store.GetMessageAsync(conn, assistantId);
            userMessage = await store.GetMessageAsync(conn, requestId);
            if (log.ProposalId is { } proposalId)
                await actions.LinkMessageAsync(proposalId, assistantId);
        }

        var conversation = (await store.GetConversationAsync(convId, user.UserId))!;
        return new SendResult(conversation, [userMessage, assistant]);
    }

    private async Task<(string Text, bool IsError)> AskModelAsync(int conversationId)
    {
        var (history, shown) = await BuildHistoryAsync(conversationId);
        var system = ChatPrompt.Build(store.Now, user.Role);
        if (shown.Count > 0)
            system += "\nWhat the user has already been shown (newest last; these are notes for you, never write them): " + string.Join(" ", shown.TakeLast(5));
        var messages = new List<AiMessage> { new(ChatRole.System, system) };
        messages.AddRange(history);

        var chatOptions = new Microsoft.Extensions.AI.ChatOptions
        {
            Tools = catalog.BuildFor(user.Role),
            Temperature = 0.1f,
            AdditionalProperties = new AdditionalPropertiesDictionary { ["think"] = false },
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        try
        {
            var response = await client.GetResponseAsync(messages, chatOptions, cts.Token);

            logger.LogDebug("Model reply: {Text} (messages: {Count}, tool rounds: {Rounds})", response.Text, response.Messages.Count,
                response.Messages.Count(m => m.Contents.OfType<FunctionCallContent>().Any()));
            var rounds = response.Messages.Count(m => m.Contents.OfType<FunctionCallContent>().Any());
            var text = ThinkStripper.StripShownNotes(ThinkStripper.Strip(response.Text));
            if (rounds >= MaxToolRounds) return (RoundLimit, true); // D22: the limit was reached without a real answer
            return (text, false);
        }
        catch (OperationCanceledException)
        {
            return (Unavailable, true);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return (ModelDownloading, true);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.ClientModel.ClientResultException)
        {
            // The OpenAI client reports HTTP failures (bad key, wrong model or endpoint) as ClientResultException.
            logger.LogWarning(ex, "The language model service could not be used");
            return (Unavailable, true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chat turn failed");
            return (SomethingWrong, true);
        }
    }

    /// <summary>
    /// The last 20 messages. Error messages are left out so an outage does not teach the model to refuse (2.6.3).
    /// What was drawn under assistant messages (tables, cards) is returned as short notes for the system prompt instead of
    /// being added to the assistant's own lines: a small model copies such lines, or earlier wording like "I have proposed ...
    /// Please confirm", instead of calling the tool again. Card turns are left out of the assistant lines for the same reason.
    /// </summary>
    private async Task<(List<AiMessage> Messages, List<string> Shown)> BuildHistoryAsync(int conversationId)
    {
        var list = new List<AiMessage>();
        var shown = new List<string>();
        foreach (var m in await store.GetRecentMessagesAsync(conversationId, HistoryMessages))
        {
            if (m.IsError) continue;
            if (m.Role == MessageRole.User)
            {
                list.Add(new AiMessage(ChatRole.User, m.Text));
                continue;
            }
            if (ChatPayloadJson.Summarise(m.PayloadKind, m.PayloadJson) is { } note) shown.Add(note);
            var isCard = m.PayloadKind is PayloadKinds.ProposalCard or PayloadKinds.FinalConfirmCard;
            if (!isCard) list.Add(new AiMessage(ChatRole.Assistant, m.Text));
        }
        return (list, shown);
    }

    public const string NoCardNote = "No confirmation card was created, so nothing has been proposed or changed. Please rephrase your request.";

    /// <summary>
    /// A small model sometimes says "please confirm" without calling a propose tool. There is then no card to click,
    /// so say so plainly instead of leaving the user waiting for one.
    /// </summary>
    private string FlagFalseConfirmation(string text) =>
        !log.ProposalMade && !string.IsNullOrWhiteSpace(text)
        && System.Text.RegularExpressions.Regex.IsMatch(text, @"(please confirm|confirm (this|the) change|I'?ve proposed|I have proposed|has been proposed)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            ? $"{text}\n{NoCardNote}"
            : text;

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
