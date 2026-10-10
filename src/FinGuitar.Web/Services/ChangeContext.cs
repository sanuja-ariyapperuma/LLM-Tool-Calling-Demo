using FinGuitar.Web.Domain;

namespace FinGuitar.Web.Services;

/// <summary>
/// Where the changes in this request come from (D15). The default is the admin panel, so pages need nothing extra.
/// Another origin calls MarkAsChat before it uses the normal write methods.
/// </summary>
public class ChangeContext
{
    public ChangeSource Source { get; private set; } = ChangeSource.AdminPanel;
    public int? ConversationId { get; private set; }
    public int? ChatMessageId { get; private set; }
    public string? ProposedActionId { get; private set; }
    public string? RequestText { get; private set; }
    public int? ConfirmationCount { get; private set; }

    public void MarkAsChat(int conversationId, int chatMessageId, string proposedActionId, string requestText, int confirmationCount)
    {
        Source = ChangeSource.Chat;
        ConversationId = conversationId;
        ChatMessageId = chatMessageId;
        ProposedActionId = proposedActionId;
        RequestText = requestText;
        ConfirmationCount = confirmationCount;
    }
}
