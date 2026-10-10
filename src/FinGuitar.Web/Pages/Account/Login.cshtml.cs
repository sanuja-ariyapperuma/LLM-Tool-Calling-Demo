using FinGuitar.Web.Services;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinGuitar.Web.Pages.Account;

public class LoginModel(AuthService auth) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Enter your email."), EmailAddress(ErrorMessage = "Enter a valid email.")]
    public string Email { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Enter your password.")]
    public string Password { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true ? RedirectToPage("/Admin/Index") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        Error = await auth.SignInAsync(Email, Password);
        if (Error is not null)
        {
            Password = "";
            return Page();
        }

        return !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)
            ? LocalRedirect(ReturnUrl)
            : RedirectToPage("/Admin/Index");
    }
}
