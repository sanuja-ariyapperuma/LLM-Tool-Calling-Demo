using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinGuitar.Web.Pages.Admin.Products;

public class DetailsModel(ProductService products, AuditService audit) : PageModel
{
    public Product Product { get; private set; } = null!;
    public IReadOnlyList<AuditEntry> History { get; private set; } = [];

    [TempData] public string? Message { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var product = await products.GetAsync(id);
        if (product is null) return NotFound();
        Product = product;
        History = await audit.GetForEntityAsync("Product", id, 5);
        return Page();
    }
}
