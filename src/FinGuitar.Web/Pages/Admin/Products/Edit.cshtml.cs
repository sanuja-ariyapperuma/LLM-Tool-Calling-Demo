using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinGuitar.Web.Pages.Admin.Products;

public class EditModel(ProductService products) : PageModel
{
    public class EditInput
    {
        public decimal Price { get; set; }
        public int Stock { get; set; }
    }

    public Product Product { get; private set; } = null!;
    [BindProperty] public EditInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var product = await products.GetAsync(id);
        if (product is null) return NotFound();
        Product = product;
        Input = new EditInput { Price = product.Price, Stock = product.Stock };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var product = await products.GetAsync(id);
        if (product is null) return NotFound();
        Product = product;

        try
        {
            await products.UpdateAsync(id, Input.Price, Input.Stock);
            TempData["Message"] = $"Updated {product.Name}.";
            return RedirectToPage("/Admin/Products/Details", new { id });
        }
        catch (ValidationException ex)
        {
            foreach (var (field, message) in ex.Errors) ModelState.AddModelError($"Input.{field}", message);
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError("", ex.Message);
        }
        return Page();
    }
}
