using FinGuitar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinGuitar.Web.Pages.Admin.Customers;

public class DetailsModel(CustomerService customers) : PageModel
{
    public CustomerDetails Details { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var details = await customers.GetDetailsAsync(id);
        if (details is null) return NotFound();
        Details = details;
        return Page();
    }
}
