using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FinGuitar.Web.Pages.Admin.Orders;

public class IndexModel(OrderService orders) : PageModel
{
    public const int PageSize = 25;

    [BindProperty(SupportsGet = true)] public int? OrderId { get; set; }
    [BindProperty(SupportsGet = true)] public OrderStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? DateFrom { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? DateTo { get; set; }
    [BindProperty(SupportsGet = true)] public string? Customer { get; set; }
    [BindProperty(SupportsGet = true)] public string? Product { get; set; }
    [BindProperty(SupportsGet = true)] public decimal? MinTotal { get; set; }
    [BindProperty(SupportsGet = true)] public decimal? MaxTotal { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public PagedResult<OrderRow> Result { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Result = await orders.SearchAsync(
            new OrderFilter(OrderId, Customer, Product, Status, DateFrom, DateTo, MinTotal, MaxTotal), PageNumber, PageSize);
    }

    public IDictionary<string, string?> RouteValues() => new Dictionary<string, string?>
    {
        ["OrderId"] = OrderId?.ToString(),
        ["Status"] = Status?.ToString(),
        ["DateFrom"] = DateFrom is { } f ? UiHelpers.Date(f) : null,
        ["DateTo"] = DateTo is { } t ? UiHelpers.Date(t) : null,
        ["Customer"] = Customer,
        ["Product"] = Product,
        ["MinTotal"] = MinTotal?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["MaxTotal"] = MaxTotal?.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
