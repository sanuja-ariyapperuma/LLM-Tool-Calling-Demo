using Dapper;
using FinGuitar.Web.Chat;

namespace FinGuitar.Tests;

/// <summary>The chat services on top of a TestEnv, for one signed-in user and one conversation.</summary>
public sealed class ChatTestEnv : IAsyncDisposable
{
    public TestEnv Env { get; }
    public ChatStore Store { get; }
    public ToolResultLog Log { get; } = new();
    public ProposedActionService Actions { get; }
    public ChatTools Tools { get; }
    public ChatToolCatalog Catalog { get; }
    public int ConversationId { get; private set; }
    public int RequestMessageId { get; private set; }

    private ChatTestEnv(TestEnv env)
    {
        Env = env;
        Store = new ChatStore(env.Db.Db, env.Db.Time);
        Actions = new ProposedActionService(env.Db.Db, Store, env.User, env.Context, env.Orders, env.Products);
        Tools = new ChatTools(env.User, env.Orders, env.Products, env.Customers, env.Sales, Actions, Log);
        Catalog = new ChatToolCatalog(Tools);
    }

    public static async Task<ChatTestEnv> CreateAsync(int? userId = TestEnv.Level1Id, DateTime? now = null)
    {
        var c = new ChatTestEnv(await TestEnv.CreateAsync(userId, now));
        await c.NewTurnAsync("test request");
        return c;
    }

    /// <summary>A second environment on the same database, signed in as someone else.</summary>
    public static ChatTestEnv Another(ChatTestEnv first, int userId) => new(TestEnv.ForUser(first.Env.Db, userId));

    /// <summary>Starts a turn like ChatService does: a conversation (new or given) and a user message, and points the log at them.</summary>
    public async Task NewTurnAsync(string text, int? conversationId = null)
    {
        await using var conn = await Env.Db.Db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        ConversationId = conversationId ?? await Store.CreateConversationAsync(conn, tx, Env.User.UserId, text);
        RequestMessageId = await Store.AddMessageAsync(conn, tx, ConversationId, MessageRole.User, text);
        await tx.CommitAsync();
        Log.ConversationId = ConversationId;
        Log.RequestMessageId = RequestMessageId;
    }

    public async Task<T> Scalar<T>(string sql, object? args = null)
    {
        await using var conn = await Env.Db.Db.OpenAsync();
        return await conn.ExecuteScalarAsync<T>(sql, args) ?? default!;
    }

    public Task Exec(string sql, object? args = null) => Scalar<int?>(sql + "; SELECT 1", args);

    public ValueTask DisposeAsync() => Env.DisposeAsync();
}
