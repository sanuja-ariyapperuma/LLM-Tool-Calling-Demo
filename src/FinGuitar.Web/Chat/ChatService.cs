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
    IChatClient client, IOptions<OllamaOptions> options, ILogger<ChatService> logger)
{
    public const int MaxMessageLength = 1000;
    public const int HistoryMessages = 20;
    public const int MaxToolRounds = 5;

    public const string Unavailable = "The assistant is not available right now. Check that Ollama is running.";
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
        var history = await BuildHistoryAsync(conversationId);
        var messages = new List<AiMessage> { new(ChatRole.System, ChatPrompt.Build(store.Now, user.Role)) };
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
        catch (HttpRequestException)
        {
            return (Unavailable, true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chat turn failed");
            return (SomethingWrong, true);
        }
    }

    /// <summary>The last 20 messages. Error messages are left out so an outage does not teach the model to refuse (2.6.3).</summary>
    private async Task<List<AiMessage>> BuildHistoryAsync(int conversationId)
    {
        var list = new List<AiMessage>();
        foreach (var m in await store.GetRecentMessagesAsync(conversationId, HistoryMessages))
        {
            if (m.IsError) continue;
            if (m.Role == MessageRole.User)
            {
                list.Add(new AiMessage(ChatRole.User, m.Text));
                continue;
            }
            var summary = ChatPayloadJson.Summarise(m.PayloadKind, m.PayloadJson);
            // A small model copies earlier wording such as "I have proposed ... Please confirm" instead of calling the
            // tool again, so a card turn goes into history as the note alone, never as that sentence.
            var isCard = m.PayloadKind is PayloadKinds.ProposalCard or PayloadKinds.FinalConfirmCard;
            list.Add(new AiMessage(ChatRole.Assistant, summary is null ? m.Text : isCard ? summary : $"{m.Text}\n{summary}"));
        }
        return list;
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
