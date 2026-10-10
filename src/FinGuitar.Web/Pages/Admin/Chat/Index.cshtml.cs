using FinGuitar.Web.Chat;
using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinGuitar.Web.Pages.Admin.Chat;

/// <summary>The view model for the card-update partial: the card's new state plus any messages the server added.</summary>
public record ActionUpdateView(ProposedAction Action, IReadOnlyList<ChatMessage> NewMessages, string? Notice);

public class IndexModel(ChatService chat, ChatStore store, ProposedActionService actions, CurrentUser user) : PageModel
{
    public IReadOnlyList<ChatConversation> Conversations { get; private set; } = [];
    public ChatConversation? Current { get; private set; }
    public IReadOnlyList<ChatMessage> Messages { get; private set; } = [];

    /// <summary>Another user's conversation id gives 404 (2.6.7).</summary>
    public async Task<IActionResult> OnGetAsync(int? c)
    {
        Conversations = await store.ListConversationsAsync(user.UserId);
        if (c is { } id)
        {
            Current = await store.GetConversationAsync(id, user.UserId);
            if (Current is null) return NotFound();
            Messages = await store.GetMessagesAsync(id);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostSendAsync(int? conversationId, string? text)
    {
        try
        {
            var result = await chat.SendAsync(conversationId, text);
            Response.Headers["X-Conversation-Id"] = result.Conversation.Id.ToString();
            return Partial("Chat/_Messages", result.NewMessages);
        }
        catch (NotFoundException) { return NotFound(); }
        catch (ValidationException ex) { return BadRequest(string.Join(" ", ex.Errors.Values)); }
    }

    public Task<IActionResult> OnPostConfirmAsync(Guid id) => Run(() => actions.ConfirmAsync(id));

    public Task<IActionResult> OnPostFinalConfirmAsync(Guid id) => Run(() => actions.FinalConfirmAsync(id));

    public Task<IActionResult> OnPostCancelAsync(Guid id) => Run(() => actions.CancelAsync(id));

    private async Task<IActionResult> Run(Func<Task<ActionOutcome>> step)
    {
        try
        {
            var outcome = await step();
            return Partial("Chat/_ActionUpdate", new ActionUpdateView(outcome.Action, outcome.NewMessages, outcome.Notice));
        }
        catch (NotFoundException) { return NotFound(); }
    }
}
