namespace FinGuitar.Web.Domain;

public static class Roles
{
    public const string Level1 = "Level1";
    public const string Level2 = "Level2";
}

public class AdminUser
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = Roles.Level2;
    public int FailedAttempts { get; set; }
    public DateTime? LockoutUntil { get; set; }
}

public enum ChangeSource { AdminPanel, Chat }
public enum AuditAction { OrderStatusChanged, ProductCreated, ProductUpdated }

public record FieldChange(string Field, string? Old, string? New);

public class AuditEntry
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public int UserId { get; set; }
    public string UserEmail { get; set; } = "";
    public string UserRole { get; set; } = "";
    public ChangeSource Source { get; set; }
    public string EntityType { get; set; } = "";
    public int EntityId { get; set; }
    public AuditAction Action { get; set; }
    public string Summary { get; set; } = "";
    public string ChangesJson { get; set; } = "[]";
    public int? ConversationId { get; set; }
    public int? ChatMessageId { get; set; }
    public string? ProposedActionId { get; set; }
    public string? ChatRequestText { get; set; }
    public int? ConfirmationCount { get; set; }
}
