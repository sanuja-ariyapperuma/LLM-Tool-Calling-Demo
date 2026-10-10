using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinGuitar.Web.Pages;

public class CartModel(CartService cart) : PageModel
{
    public CartView View { get; private set; } = null!;

    [TempData] public string? Notice { get; set; }

    public async Task OnGetAsync() => View = await cart.GetAsync();

    public async Task<IActionResult> OnPostAddAsync(int productId)
    {
        try { await cart.AddAsync(productId); }
        catch (DomainException ex) { Notice = ex.Message; }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int productId, int quantity)
    {
        await cart.UpdateAsync(productId, quantity);
        return RedirectToPage();
    }

    public IActionResult OnPostRemove(int productId)
    {
        cart.Remove(productId);
        return RedirectToPage();
    }
}
