namespace FinGuitar.Web.Chat;

/// <summary>
/// Scoped, one per request. Holds what the tools returned during this turn so the server can draw it (FR-C3),
/// plus the turn's ids so a proposal knows its conversation and request message (P6).
/// </summary>
public class ToolResultLog
{
    private readonly List<ChatPayload> _payloads = [];

    public int ConversationId { get; set; }
    public int RequestMessageId { get; set; }

    public Guid? ProposalId { get; private set; }
    public ChatPayload? ProposalPayload { get; private set; }

    public bool ProposalMade => ProposalId is not null;

    public void Add(string kind, object data) => _payloads.Add(new ChatPayload(kind, data));

    public void AddProposal(Guid id, ProposalCardPayload payload)
    {
        ProposalId = id;
        ProposalPayload = new ChatPayload(PayloadKinds.ProposalCard, payload);
    }

    /// <summary>The payload rule (2.6.3): the proposal card if there is one, otherwise the last successful data result.</summary>
    public ChatPayload? Choose() => ProposalPayload ?? _payloads.LastOrDefault();
}
