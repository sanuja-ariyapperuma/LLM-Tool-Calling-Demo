using Microsoft.Extensions.AI;

namespace FinGuitar.Tests;

/// <summary>A fake model: the test decides what it answers. Wrapped in the real function-invocation client, so tool calls really run.</summary>
public sealed class ScriptedClient(Func<IList<ChatMessage>, ChatOptions?, ChatResponse> respond) : IChatClient
{
    public List<(IList<ChatMessage> Messages, ChatOptions? Options)> Seen { get; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        Seen.Add((list, options));
        return Task.FromResult(respond(list, options));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    // ---- helpers to build responses ----------------------------------------------------------------------------

    public static ChatResponse Say(string text) => new(new ChatMessage(ChatRole.Assistant, text));

    public static ChatResponse Call(string tool, params (string Key, object? Value)[] args) =>
        new(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent(Guid.NewGuid().ToString("N"), tool, args.ToDictionary(a => a.Key, a => a.Value))]));

    public static bool LastIsToolResult(IList<ChatMessage> messages) => messages.Count > 0 && messages[^1].Role == ChatRole.Tool;

    public static string LastUserText(IList<ChatMessage> messages) =>
        messages.Last(m => m.Role == ChatRole.User).Text;

    /// <summary>Plays the part of a sensible model for the page tests: one tool call chosen from the user's words, then a short sentence.</summary>
    public static ChatResponse Sensible(IList<ChatMessage> messages, ChatOptions? options)
    {
        if (LastIsToolResult(messages)) return Say("Done. See below.");
        var text = LastUserText(messages).ToLowerInvariant();
        if (text.Contains("pending")) return Call("search_orders", ("status", "Pending"));
        if (text.Contains("order 42")) return Call("propose_order_status_change", ("orderId", 42), ("newStatus", "Dispatched"));
        if (text.Contains("cancel 43")) return Call("propose_order_status_change", ("orderId", 43), ("newStatus", "Cancelled"));
        if (text.Contains("cancel 44")) return Call("propose_order_status_change", ("orderId", 44), ("newStatus", "Cancelled"));
        return Say("Hello.");
    }
}
