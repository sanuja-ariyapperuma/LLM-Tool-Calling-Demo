using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinGuitar.Web.Pages;

public class IndexModel(ProductService products) : PageModel
{
    public IReadOnlyList<Product> Products { get; private set; } = [];

    public async Task OnGetAsync() =>
        Products = (await products.SearchAsync(new ProductFilter(), 1, 100)).Rows;
}
