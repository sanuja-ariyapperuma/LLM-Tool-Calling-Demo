using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinGuitar.Web.Pages.Admin.Products;

public class CreateModel(ProductService products) : PageModel
{
    [BindProperty] public ProductInput Input { get; set; } = new() { Category = "Electric", Price = 100, Stock = 1 };

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            var id = await products.CreateAsync(Input);
            TempData["Message"] = $"Created {Input.Name}.";
            return RedirectToPage("/Admin/Products/Details", new { id });
        }
        catch (ValidationException ex)
        {
            foreach (var (field, message) in ex.Errors) ModelState.AddModelError($"Input.{field}", message);
            return Page();
        }
    }
}
