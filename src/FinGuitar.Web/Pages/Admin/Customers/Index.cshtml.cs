using FinGuitar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinGuitar.Web.Pages.Admin.Customers;

public class IndexModel(CustomerService customers) : PageModel
{
    public const int PageSize = 20;

    [BindProperty(SupportsGet = true)] public string? Text { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public PagedResult<CustomerRow> Result { get; private set; } = null!;

    public async Task OnGetAsync() => Result = await customers.SearchAsync(Text, PageNumber, PageSize);

    public IDictionary<string, string?> RouteValues() => new Dictionary<string, string?> { ["Text"] = Text };
}
