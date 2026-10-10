namespace FinGuitar.Web.Chat;

public enum MessageRole { User, Assistant, ActionResult }

public enum ProposedActionType { OrderStatusChange, ProductUpdate }

public enum ProposedActionStatus { Pending, AwaitingFinalConfirm, Applying, Confirmed, Cancelled, Expired, Failed }

public class ChatConversation
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ChatMessage
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public MessageRole Role { get; set; }
    public string Text { get; set; } = "";
    public string? PayloadKind { get; set; }
    public string? PayloadJson { get; set; }
    public bool IsError { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ProposedAction
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public int ConversationId { get; set; }
    public int? MessageId { get; set; }
    public int RequestMessageId { get; set; }
    public ProposedActionType ActionType { get; set; }
    public string ArgumentsJson { get; set; } = "{}";
    public string Summary { get; set; } = "";
    public bool IsCritical { get; set; }
    public string? CriticalWarning { get; set; }
    public ProposedActionStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FirstConfirmedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResultText { get; set; }

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>The status to show: a card that is still open but older than 10 minutes counts as Expired (D2).</summary>
    public ProposedActionStatus EffectiveStatus(DateTime now) =>
        Status is ProposedActionStatus.Pending or ProposedActionStatus.AwaitingFinalConfirm && now - CreatedAt > Lifetime
            ? ProposedActionStatus.Expired
            : Status;
}
