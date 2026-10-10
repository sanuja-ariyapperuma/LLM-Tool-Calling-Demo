using System.Data.Common;
using System.Text.Json;
using Dapper;
using FinGuitar.Web.Data;
using FinGuitar.Web.Domain;

namespace FinGuitar.Web.Services;

public record AuditChange(string EntityType, int EntityId, AuditAction Action, string Summary, IReadOnlyList<FieldChange> Changes);

public class AuditUser
{
    public int UserId { get; set; }
    public string UserEmail { get; set; } = "";
}

/// <summary>
/// Writes and reads the append-only audit log (D15). RecordAsync is called only by the write methods of the
/// services, inside the same transaction as the change, so a change and its entry commit or roll back together.
/// </summary>
public class AuditService(Db db, TimeProvider time, CurrentUser user, ChangeContext context)
{
    private const string Columns = @"id, occurred_at, user_id, user_email, user_role, source, entity_type, entity_id, action,
        summary, changes_json, conversation_id, chat_message_id, proposed_action_id, chat_request_text, confirmation_count";

    public async Task RecordAsync(DbConnection conn, DbTransaction tx, AuditChange change)
    {
        await conn.ExecuteAsync(
            @"INSERT INTO audit_entries (occurred_at, user_id, user_email, user_role, source, entity_type, entity_id, action,
                    summary, changes_json, conversation_id, chat_message_id, proposed_action_id, chat_request_text, confirmation_count)
              VALUES (@occurredAt, @userId, @email, @role, @source, @entityType, @entityId, @action,
                    @summary, @changesJson, @conversationId, @chatMessageId, @proposedActionId, @requestText, @confirmationCount)",
            new
            {
                occurredAt = time.GetLocalNow().DateTime,
                userId = user.UserId,
                email = user.Email,
                role = user.Role,
                source = context.Source.ToString(),
                entityType = change.EntityType,
                entityId = change.EntityId,
                action = change.Action.ToString(),
                summary = change.Summary,
                changesJson = JsonSerializer.Serialize(change.Changes),
                conversationId = context.ConversationId,
                chatMessageId = context.ChatMessageId,
                proposedActionId = context.ProposedActionId,
                requestText = context.RequestText,
                confirmationCount = context.ConfirmationCount,
            }, tx);
    }

    public async Task<PagedResult<AuditEntry>> SearchAsync(AuditFilter filter, int page, int pageSize)
    {
        var where = new List<string>();
        var p = new DynamicParameters();
        if (filter.DateFrom is { } from) { where.Add("occurred_at >= @from"); p.Add("from", from.Date); }
        if (filter.DateTo is { } to) { where.Add("occurred_at < @toExclusive"); p.Add("toExclusive", to.Date.AddDays(1)); }
        if (filter.Source is { } source) { where.Add("source = @source"); p.Add("source", source.ToString()); }
        if (filter.UserId is { } userId) { where.Add("user_id = @userId"); p.Add("userId", userId); }
        if (!string.IsNullOrWhiteSpace(filter.EntityType)) { where.Add("entity_type = @entityType"); p.Add("entityType", filter.EntityType); }
        if (filter.EntityId is { } entityId) { where.Add("entity_id = @entityId"); p.Add("entityId", entityId); }
        var clause = where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where);

        p.Add("limit", pageSize);
        p.Add("offset", Sql.Offset(page, pageSize));

        await using var conn = await db.OpenAsync();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM audit_entries {clause}", p);
        var rows = (await conn.QueryAsync<AuditEntry>(
            $"SELECT {Columns} FROM audit_entries {clause} ORDER BY occurred_at DESC, id DESC LIMIT @limit OFFSET @offset", p)).ToList();
        return new PagedResult<AuditEntry>(rows, total, Math.Max(1, page), pageSize);
    }

    public async Task<IReadOnlyList<AuditEntry>> GetForEntityAsync(string entityType, int entityId, int count)
    {
        await using var conn = await db.OpenAsync();
        return (await conn.QueryAsync<AuditEntry>(
            $"SELECT {Columns} FROM audit_entries WHERE entity_type = @entityType AND entity_id = @entityId ORDER BY occurred_at DESC, id DESC LIMIT @count",
            new { entityType, entityId, count })).ToList();
    }

    /// <summary>The distinct users that appear in the log, for the audit page's user filter.</summary>
    public async Task<IReadOnlyList<AuditUser>> GetUsersAsync()
    {
        await using var conn = await db.OpenAsync();
        return (await conn.QueryAsync<AuditUser>(
            "SELECT DISTINCT user_id AS UserId, user_email AS UserEmail FROM audit_entries ORDER BY user_email")).ToList();
    }

    public static IReadOnlyList<FieldChange> ParseChanges(string json) =>
        JsonSerializer.Deserialize<List<FieldChange>>(json) ?? [];
}
