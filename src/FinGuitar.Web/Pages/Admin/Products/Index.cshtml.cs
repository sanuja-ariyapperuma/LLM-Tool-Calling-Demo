using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinGuitar.Web.Pages.Admin.Products;

public class IndexModel(ProductService products) : PageModel
{
    public const int PageSize = 20;

    [BindProperty(SupportsGet = true)] public string? Text { get; set; }
    [BindProperty(SupportsGet = true)] public bool LowStockOnly { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public PagedResult<Product> Result { get; private set; } = null!;

    public async Task OnGetAsync() =>
        Result = await products.SearchAsync(new ProductFilter(Text, LowStockOnly), PageNumber, PageSize);

    public IDictionary<string, string?> RouteValues() => new Dictionary<string, string?>
    {
        ["Text"] = Text,
        ["LowStockOnly"] = LowStockOnly ? "true" : null,
    };
}
