using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinGuitar.Web.Pages.Admin;

public class IndexModel(SalesService sales, OrderService orders, TimeProvider time) : PageModel
{
    public DashboardData Dashboard { get; private set; } = null!;
    public PagedResult<OrderRow> Today { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        var today = time.GetLocalNow().DateTime.Date;
        Dashboard = await sales.GetDashboardAsync();
        Today = await orders.SearchAsync(new OrderFilter(DateFrom: today, DateTo: today), 1, 25);
    }
}
