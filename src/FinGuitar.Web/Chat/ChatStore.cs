using System.Data.Common;
using Dapper;
using FinGuitar.Web.Data;

namespace FinGuitar.Web.Chat;

/// <summary>Reads and writes the chat's own tables (conversations and messages). These are the chat's, so it may use SQL on them (NFR-11).</summary>
public class ChatStore(Db db, TimeProvider time)
{
    public const string MessageColumns = "id, conversation_id, role, text, payload_kind, payload_json, is_error, created_at";
    private const string ConversationColumns = "id, user_id, title, created_at, updated_at";

    public DateTime Now => time.GetLocalNow().DateTime;

    public async Task<int> CreateConversationAsync(DbConnection conn, DbTransaction tx, int userId, string title)
    {
        var now = Now;
        return await conn.ExecuteScalarAsync<int>(
            @"INSERT INTO chat_conversations (user_id, title, created_at, updated_at) VALUES (@userId, @title, @now, @now);
              SELECT last_insert_rowid();", new { userId, title, now }, tx);
    }

    public async Task<int> AddMessageAsync(DbConnection conn, DbTransaction? tx, int conversationId, MessageRole role, string text,
        string? payloadKind = null, string? payloadJson = null, bool isError = false)
    {
        var now = Now;
        var id = await conn.ExecuteScalarAsync<int>(
            @"INSERT INTO chat_messages (conversation_id, role, text, payload_kind, payload_json, is_error, created_at)
              VALUES (@conversationId, @role, @text, @payloadKind, @payloadJson, @isError, @now);
              SELECT last_insert_rowid();",
            new { conversationId, role = role.ToString(), text, payloadKind, payloadJson, isError = isError ? 1 : 0, now }, tx);
        await conn.ExecuteAsync("UPDATE chat_conversations SET updated_at = @now WHERE id = @conversationId",
            new { now, conversationId }, tx);
        return id;
    }

    public async Task<ChatMessage> GetMessageAsync(DbConnection conn, int id) =>
        await conn.QuerySingleAsync<ChatMessage>($"SELECT {MessageColumns} FROM chat_messages WHERE id = @id", new { id });

    /// <summary>The conversation, or null when it does not exist or belongs to someone else (shown as 404).</summary>
    public async Task<ChatConversation?> GetConversationAsync(int id, int userId)
    {
        await using var conn = await db.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<ChatConversation>(
            $"SELECT {ConversationColumns} FROM chat_conversations WHERE id = @id AND user_id = @userId", new { id, userId });
    }

    public async Task<IReadOnlyList<ChatConversation>> ListConversationsAsync(int userId)
    {
        await using var conn = await db.OpenAsync();
        return (await conn.QueryAsync<ChatConversation>(
            $"SELECT {ConversationColumns} FROM chat_conversations WHERE user_id = @userId ORDER BY updated_at DESC, id DESC",
            new { userId })).ToList();
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(int conversationId)
    {
        await using var conn = await db.OpenAsync();
        return (await conn.QueryAsync<ChatMessage>(
            $"SELECT {MessageColumns} FROM chat_messages WHERE conversation_id = @conversationId ORDER BY id", new { conversationId })).ToList();
    }

    /// <summary>The newest messages for the model's history, oldest first.</summary>
    public async Task<IReadOnlyList<ChatMessage>> GetRecentMessagesAsync(int conversationId, int count)
    {
        await using var conn = await db.OpenAsync();
        var rows = (await conn.QueryAsync<ChatMessage>(
            $"SELECT {MessageColumns} FROM chat_messages WHERE conversation_id = @conversationId ORDER BY id DESC LIMIT @count",
            new { conversationId, count })).ToList();
        rows.Reverse();
        return rows;
    }
}
