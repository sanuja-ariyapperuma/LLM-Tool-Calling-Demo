using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinGuitar.Web.Pages.Admin.Orders;

public class DetailsModel(OrderService orders, AuditService audit) : PageModel
{
    public OrderDetails Details { get; private set; } = null!;
    public IReadOnlyList<OrderStatus> AllowedNext { get; private set; } = [];
    public IReadOnlyList<AuditEntry> History { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? Confirm { get; set; }

    [TempData] public string? Message { get; set; }

    public bool ShowCancelPanel => Confirm == "cancel" && AllowedNext.Contains(OrderStatus.Cancelled) && User.IsInRole(Roles.Level1);

    public async Task<IActionResult> OnGetAsync(int id)
    {
        return await LoadAsync(id) ? Page() : NotFound();
    }

    /// <summary>Open to every admin; OrderService enforces Level1 and the status rules (FR-A3, FR-D1).</summary>
    public async Task<IActionResult> OnPostStatusAsync(int id, OrderStatus newStatus)
    {
        try
        {
            await orders.ChangeStatusAsync(id, newStatus);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError("", ex.Message);
            return await LoadAsync(id) ? Page() : NotFound();
        }

        Message = $"Order {id} is now {newStatus}.";
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(int id)
    {
        var details = await orders.GetDetailsAsync(id);
        if (details is null) return false;
        Details = details;
        AllowedNext = orders.GetAllowedNextStatuses(details.Order);
        History = await audit.GetForEntityAsync("Order", id, 5);
        return true;
    }
}
