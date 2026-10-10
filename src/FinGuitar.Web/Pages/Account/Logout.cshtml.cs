using FinGuitar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinGuitar.Web.Pages.Account;

/// <summary>Signing out is a POST only (FR-A1); a GET is refused.</summary>
public class LogoutModel(AuthService auth) : PageModel
{
    public IActionResult OnGet() => StatusCode(StatusCodes.Status405MethodNotAllowed);

    public async Task<IActionResult> OnPostAsync()
    {
        await auth.SignOutAsync();
        return RedirectToPage("/Account/Login");
    }
}
