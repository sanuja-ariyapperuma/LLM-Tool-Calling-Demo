using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinGuitar.Web.Pages.Products;

public class DetailsModel(ProductService products) : PageModel
{
    public Product Product { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var product = await products.GetAsync(id);
        if (product is null) return NotFound();
        Product = product;
        return Page();
    }
}
