using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinGuitar.Web.Pages.Admin.Audit;

public class IndexModel(AuditService audit) : PageModel
{
    public const int PageSize = 50;

    [BindProperty(SupportsGet = true)] public DateTime? DateFrom { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? DateTo { get; set; }
    [BindProperty(SupportsGet = true)] public ChangeSource? Source { get; set; }
    [BindProperty(SupportsGet = true)] public int? UserId { get; set; }
    [BindProperty(SupportsGet = true)] public string? EntityType { get; set; }
    [BindProperty(SupportsGet = true)] public int? EntityId { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public PagedResult<AuditEntry> Result { get; private set; } = null!;
    public IReadOnlyList<AuditUser> Users { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Result = await audit.SearchAsync(new AuditFilter(DateFrom, DateTo, Source, UserId, EntityType, EntityId), PageNumber, PageSize);
        Users = await audit.GetUsersAsync();
    }

    public IReadOnlyList<FieldChange> ChangesOf(AuditEntry e) => AuditService.ParseChanges(e.ChangesJson);

    public IDictionary<string, string?> RouteValues() => new Dictionary<string, string?>
    {
        ["DateFrom"] = DateFrom is { } f ? UiHelpers.Date(f) : null,
        ["DateTo"] = DateTo is { } t ? UiHelpers.Date(t) : null,
        ["Source"] = Source?.ToString(),
        ["UserId"] = UserId?.ToString(),
        ["EntityType"] = EntityType,
        ["EntityId"] = EntityId?.ToString(),
    };
}
