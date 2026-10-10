using System.ComponentModel;
using System.Globalization;
using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;

namespace FinGuitar.Web.Chat;

/// <summary>
/// One method per tool. Each is a thin adapter (2.6.2): check the role, parse the model's arguments, call ONE existing
/// service method, return a compact result to the model, and record the full result for the server to draw.
/// No business logic lives here. Errors go back as { error } so the model can explain them (FR-C12).
/// </summary>
public class ChatTools(
    CurrentUser user, OrderService orders, ProductService products, CustomerService customers, SalesService sales,
    ProposedActionService actions, ToolResultLog log)
{
    public const int MaxRows = 20;

    private static object Error(string message) => new { error = message };

    private static readonly string StatusList = string.Join(", ", Enum.GetNames<OrderStatus>());

    // ---- argument parsing -------------------------------------------------------------------------------------

    private static bool Given(string? s) => !string.IsNullOrWhiteSpace(s);

    private static bool TryDate(string? s, string name, out DateTime? value, out object? error)
    {
        value = null; error = null;
        if (!Given(s)) return true;
        if (DateTime.TryParseExact(s!.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) { value = d; return true; }
        error = Error($"{name} must be a date written as yyyy-MM-dd, for example 2026-10-09.");
        return false;
    }

    private static bool TryDecimal(string? s, string name, out decimal? value, out object? error)
    {
        value = null; error = null;
        if (!Given(s)) return true;
        if (decimal.TryParse(s!.Trim().TrimStart('$'), NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) { value = d; return true; }
        error = Error($"{name} must be a number.");
        return false;
    }

    private static bool TryInt(string? s, string name, out int? value, out object? error)
    {
        value = null; error = null;
        if (!Given(s)) return true;
        if (int.TryParse(s!.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var d)) { value = d; return true; }
        error = Error($"{name} must be a whole number.");
        return false;
    }

    private static bool TryStatus(string? s, out OrderStatus status, out object? error)
    {
        error = null;
        if (Enum.TryParse(s?.Trim(), ignoreCase: true, out status) && Enum.IsDefined(status)) return true;
        error = Error($"Unknown status '{s}'. Valid statuses: {StatusList}.");
        return false;
    }

    private static string D(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static OrderTableRow ToRow(OrderRow o) => new(o.Id, o.CustomerName, D(o.OrderDate), o.Status.ToString(), o.Total, o.Items);

    // ---- read tools -------------------------------------------------------------------------------------------

    [Description("Search orders. Every argument is optional; leave out what the user did not mention. Returns at most 20 orders, newest first.")]
    public async Task<object> SearchOrders(
        [Description("Order id, if the user gave one")] string? orderId = null,
        [Description("Part of the customer's name, for example 'John'")] string? customerName = null,
        [Description("Part of a product name, for example 'Les Paul' or 'Strat'")] string? productName = null,
        [Description("Pending, Dispatched, Delivered or Cancelled")] string? status = null,
        [Description("First order date, written yyyy-MM-dd")] string? dateFrom = null,
        [Description("Last order date, written yyyy-MM-dd (the whole day is included)")] string? dateTo = null,
        [Description("Minimum order total")] string? minTotal = null,
        [Description("Maximum order total")] string? maxTotal = null)
    {
        if (!TryInt(orderId, "orderId", out var id, out var e1)) return e1!;
        if (!TryDate(dateFrom, "dateFrom", out var from, out var e2)) return e2!;
        if (!TryDate(dateTo, "dateTo", out var to, out var e3)) return e3!;
        if (!TryDecimal(minTotal, "minTotal", out var min, out var e4)) return e4!;
        if (!TryDecimal(maxTotal, "maxTotal", out var max, out var e5)) return e5!;
        OrderStatus? st = null;
        if (Given(status))
        {
            if (!TryStatus(status, out var parsed, out var e6)) return e6!;
            st = parsed;
        }

        var result = await orders.SearchAsync(new OrderFilter(id is > 0 ? id : null, customerName, productName, st, from, to, min, max is > 0 ? max : null), 1, MaxRows);
        var rows = result.Rows.Select(ToRow).ToList();
        log.Add(PayloadKinds.OrderTable, new OrderTablePayload(rows, result.TotalCount));
        return new
        {
            totalCount = result.TotalCount,
            showing = rows.Count,
            orders = rows.Select(r => new { id = r.Id, customer = r.Customer, date = r.Date, status = r.Status, total = r.Total, items = r.Items }),
        };
    }

    [Description("Get the full details of one order: customer, items, status, total.")]
    public async Task<object> GetOrderDetails([Description("The order id")] int orderId)
    {
        var d = await orders.GetDetailsAsync(orderId);
        if (d is null) return Error($"Order {orderId} not found.");

        var o = d.Order;
        var payload = new OrderDetailPayload(o.Id, d.Customer.Id, d.Customer.Name, D(o.OrderDate), o.Status.ToString(), o.Total,
            o.Lines.Select(l => new OrderLinePayload(l.ProductId, l.ProductName, l.Quantity, l.UnitPrice)).ToList());
        log.Add(PayloadKinds.OrderDetail, payload);
        return new
        {
            id = o.Id, customer = d.Customer.Name, date = payload.Date, status = payload.Status, total = o.Total,
            items = payload.Lines.Select(l => new { product = l.Product, quantity = l.Quantity, unitPrice = l.UnitPrice }),
            allowedNextStatuses = orders.GetAllowedNextStatuses(o).Select(s => s.ToString()),
        };
    }

    [Description("Search products by name, brand or category, or list the products that are low on stock (3 or fewer). Returns at most 20.")]
    public async Task<object> SearchProducts(
        [Description("Part of a name, brand or category, for example 'Fender' or 'bass'")] string? text = null,
        [Description("true to list only low-stock products")] bool lowStockOnly = false)
    {
        var result = await products.SearchAsync(new ProductFilter(text, lowStockOnly), 1, MaxRows);
        var rows = result.Rows.Select(p => new ProductTableRow(p.Id, p.Name, p.Brand, p.Category, p.Price, p.Stock)).ToList();
        log.Add(PayloadKinds.ProductTable, new ProductTablePayload(rows, result.TotalCount));
        return new { totalCount = result.TotalCount, showing = rows.Count, products = rows };
    }

    [Description("Search customers by name, email or phone. Returns at most 20.")]
    public async Task<object> SearchCustomers([Description("Part of a name, email or phone number")] string? text = null)
    {
        var result = await customers.SearchAsync(text, 1, MaxRows);
        var rows = result.Rows.Select(c => new CustomerTableRow(c.Id, c.Name, c.Email, c.Phone, c.OrderCount)).ToList();
        log.Add(PayloadKinds.CustomerTable, new CustomerTablePayload(rows, result.TotalCount));
        return new { totalCount = result.TotalCount, showing = rows.Count, customers = rows };
    }

    [Description("Get one customer's contact details and order history.")]
    public async Task<object> GetCustomerDetails([Description("The customer id")] int customerId)
    {
        var d = await customers.GetDetailsAsync(customerId);
        if (d is null) return Error($"Customer {customerId} not found.");

        var orderRows = d.Orders.Take(MaxRows).Select(ToRow).ToList();
        var c = d.Customer;
        log.Add(PayloadKinds.CustomerDetail, new CustomerDetailPayload(c.Id, c.Name, c.Email, c.Phone, c.Address, orderRows));
        return new { id = c.Id, name = c.Name, email = c.Email, phone = c.Phone, orderCount = d.Orders.Count, orders = orderRows };
    }

    [Description("Sales summary for a date range: number of orders and revenue. Cancelled orders are not counted.")]
    public async Task<object> GetSalesSummary(
        [Description("First day, written yyyy-MM-dd")] string dateFrom,
        [Description("Last day, written yyyy-MM-dd (included)")] string dateTo)
    {
        if (!Given(dateFrom) || !Given(dateTo)) return Error("dateFrom and dateTo are both required, written yyyy-MM-dd.");
        if (!TryDate(dateFrom, "dateFrom", out var from, out var e1)) return e1!;
        if (!TryDate(dateTo, "dateTo", out var to, out var e2)) return e2!;
        if (to < from) return Error("dateTo must not be before dateFrom.");

        var s = await sales.GetSummaryAsync(from!.Value, to!.Value);
        log.Add(PayloadKinds.SalesSummary, new SalesSummaryPayload(D(s.From), D(s.To), s.OrderCount, s.Revenue));
        return new { from = D(s.From), to = D(s.To), orderCount = s.OrderCount, revenue = s.Revenue };
    }

    [Description("The best-selling products in a date range, by units sold. Cancelled orders are not counted.")]
    public async Task<object> GetTopProducts(
        [Description("First day, written yyyy-MM-dd")] string dateFrom,
        [Description("Last day, written yyyy-MM-dd (included)")] string dateTo,
        [Description("How many products, 1 to 10 (default 5)")] string? count = null)
    {
        if (!Given(dateFrom) || !Given(dateTo)) return Error("dateFrom and dateTo are both required, written yyyy-MM-dd.");
        if (!TryDate(dateFrom, "dateFrom", out var from, out var e1)) return e1!;
        if (!TryDate(dateTo, "dateTo", out var to, out var e2)) return e2!;
        if (!TryInt(count, "count", out var n, out var e3)) return e3!;
        if (to < from) return Error("dateTo must not be before dateFrom.");

        var top = await sales.GetTopProductsAsync(from!.Value, to!.Value, Math.Clamp(n ?? 5, 1, 10));
        var rows = top.Select(t => new TopProductRow(t.ProductId, t.Name, t.Units, t.Revenue)).ToList();
        log.Add(PayloadKinds.TopProducts, new TopProductsPayload(D(from.Value), D(to.Value), rows));
        return new { from = D(from.Value), to = D(to.Value), products = rows };
    }

    // ---- write tools: they only propose; nothing changes until the user clicks Confirm ------------------------

    private bool Refuse(out object? error)
    {
        error = null;
        if (!user.IsLevel1) { error = Error("You do not have permission to change data. Your account is read only."); return true; }
        if (log.ProposalMade) { error = Error("One change at a time. Ask the user to confirm this one first."); return true; }
        return false;
    }

    [Description("Propose changing an order's status. This does NOT change anything: it shows the user a Confirm button. Allowed changes: Pending to Dispatched, Dispatched to Delivered, Pending to Cancelled.")]
    public async Task<object> ProposeOrderStatusChange(
        [Description("The order id")] int orderId,
        [Description("Dispatched, Delivered or Cancelled")] string newStatus)
    {
        if (Refuse(out var refused)) return refused!;
        if (!TryStatus(newStatus, out var to, out var e1)) return e1!;

        var d = await orders.GetDetailsAsync(orderId);
        if (d is null) return Error($"Order {orderId} not found.");

        var original = d.Order.Status;
        try { d.Order.ChangeStatus(to); } // the same rule the service applies; this copy is never saved
        catch (DomainException ex) { return Error(ex.Message); }

        var summary = $"Change order {orderId} from {original} to {to}";
        var action = await actions.ProposeAsync(log.ConversationId, log.RequestMessageId, ProposedActionType.OrderStatusChange,
            new OrderStatusArgs(orderId, original, to), summary);
        return Proposed(action);
    }

    [Description("Propose a new price and/or new stock for a product. This does NOT change anything: it shows the user a Confirm button. Give at least one of newPrice or newStock.")]
    public async Task<object> ProposeProductUpdate(
        [Description("The product id")] int productId,
        [Description("The new price, for example 899.99")] string? newPrice = null,
        [Description("The new stock, a whole number")] string? newStock = null)
    {
        if (Refuse(out var refused)) return refused!;
        if (!TryDecimal(newPrice, "newPrice", out var price, out var e1)) return e1!;
        if (!TryInt(newStock, "newStock", out var stock, out var e2)) return e2!;
        if (price is null && stock is null) return Error("Give a new price, a new stock, or both.");

        var p = await products.GetAsync(productId);
        if (p is null) return Error($"Product {productId} not found.");

        try { ProductRules.Validate(price ?? p.Price, stock ?? p.Stock); }
        catch (ValidationException ex) { return Error(string.Join(" ", ex.Errors.Values)); }

        var priceSame = price is null || Data.Money.ToCents(price.Value) == p.PriceCents;
        var stockSame = stock is null || stock == p.Stock;
        if (priceSame && stockSame) return Error($"No change: Product {productId} already has that price and stock.");

        var parts = new List<string>();
        if (!priceSame) parts.Add($"price {UiHelpers.Money(p.Price)} → {UiHelpers.Money(price!.Value)}");
        if (!stockSame) parts.Add($"stock {p.Stock} → {stock}");
        var summary = $"Product {p.Id} {p.Name}: {string.Join(", ", parts)}";

        var args = new ProductUpdateArgs(p.Id, p.Name, p.Price, p.Stock, priceSame ? null : price, stockSame ? null : stock);
        var action = await actions.ProposeAsync(log.ConversationId, log.RequestMessageId, ProposedActionType.ProductUpdate, args, summary);
        return Proposed(action);
    }

    private object Proposed(ProposedAction action)
    {
        log.AddProposal(action.Id, new ProposalCardPayload(action.Id, action.Summary, action.IsCritical));
        return new
        {
            proposalId = action.Id,
            summary = action.Summary,
            critical = action.IsCritical,
            note = "Waiting for the user to click Confirm. Nothing has changed yet.",
        };
    }
}
