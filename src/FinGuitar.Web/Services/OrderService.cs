using Dapper;
using FinGuitar.Web.Data;
using FinGuitar.Web.Domain;

namespace FinGuitar.Web.Services;

public class OrderRow
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public DateTime OrderDate { get; set; }
    public OrderStatus Status { get; set; }
    public long TotalCents { get; set; }
    public decimal Total => Money.FromCents(TotalCents);
    public string Items { get; set; } = "";
}

public record OrderDetails(Order Order, Customer Customer);

public class OrderService(Db db, CurrentUser user, AuditService audit)
{
    private const string RowSelect = @"SELECT o.id, o.customer_id, c.name AS customer_name, o.order_date, o.status, o.total_cents,
        COALESCE((SELECT group_concat(p.name, ', ') FROM order_lines l JOIN products p ON p.id = l.product_id WHERE l.order_id = o.id), '') AS items
        FROM orders o JOIN customers c ON c.id = o.customer_id";

    public async Task<PagedResult<OrderRow>> SearchAsync(OrderFilter filter, int page, int pageSize)
    {
        var where = new List<string>();
        var p = new DynamicParameters();
        if (filter.OrderId is { } id) { where.Add("o.id = @orderId"); p.Add("orderId", id); }
        if (!string.IsNullOrWhiteSpace(filter.CustomerName))
        {
            where.Add("c.name LIKE @customerName ESCAPE '\\'");
            p.Add("customerName", Sql.Contains(filter.CustomerName));
        }
        if (!string.IsNullOrWhiteSpace(filter.ProductName))
        {
            where.Add(@"EXISTS (SELECT 1 FROM order_lines l JOIN products pr ON pr.id = l.product_id
                                WHERE l.order_id = o.id AND pr.name LIKE @productName ESCAPE '\')");
            p.Add("productName", Sql.Contains(filter.ProductName));
        }
        if (filter.Status is { } status) { where.Add("o.status = @status"); p.Add("status", status.ToString()); }
        if (filter.DateFrom is { } from) { where.Add("o.order_date >= @dateFrom"); p.Add("dateFrom", from.Date); }
        if (filter.DateTo is { } to) { where.Add("o.order_date < @dateToExclusive"); p.Add("dateToExclusive", to.Date.AddDays(1)); }
        if (filter.MinTotal is { } min) { where.Add("o.total_cents >= @minCents"); p.Add("minCents", Money.ToCents(min)); }
        if (filter.MaxTotal is { } max) { where.Add("o.total_cents <= @maxCents"); p.Add("maxCents", Money.ToCents(max)); }
        var clause = where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where);

        p.Add("limit", pageSize);
        p.Add("offset", Sql.Offset(page, pageSize));

        await using var conn = await db.OpenAsync();
        var total = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM orders o JOIN customers c ON c.id = o.customer_id {clause}", p);
        var rows = (await conn.QueryAsync<OrderRow>(
            $"{RowSelect} {clause} ORDER BY o.order_date DESC, o.id DESC LIMIT @limit OFFSET @offset", p)).ToList();
        return new PagedResult<OrderRow>(rows, total, Math.Max(1, page), pageSize);
    }

    public async Task<OrderDetails?> GetDetailsAsync(int id)
    {
        await using var conn = await db.OpenAsync();
        var order = await conn.QuerySingleOrDefaultAsync<Order>(
            "SELECT id, customer_id, order_date, status, total_cents FROM orders WHERE id = @id", new { id });
        if (order is null) return null;

        order.Lines = (await conn.QueryAsync<OrderLine>(
            @"SELECT l.id, l.order_id, l.product_id, p.name AS product_name, l.quantity, l.unit_price_cents
              FROM order_lines l JOIN products p ON p.id = l.product_id WHERE l.order_id = @id ORDER BY l.id", new { id })).ToList();
        var customer = await conn.QuerySingleAsync<Customer>(
            "SELECT id, name, email, phone, address FROM customers WHERE id = @customerId", new { customerId = order.CustomerId });
        return new OrderDetails(order, customer);
    }

    public IReadOnlyList<OrderStatus> GetAllowedNextStatuses(Order order) => OrderStatusRules.AllowedNext(order.Status);

    /// <summary>Level1 only. The rule check, the update, and the audit entry share one transaction (2.4.1).</summary>
    public async Task ChangeStatusAsync(int id, OrderStatus newStatus)
    {
        user.RequireLevel1();

        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var order = await conn.QuerySingleOrDefaultAsync<Order>(
            "SELECT id, customer_id, order_date, status, total_cents FROM orders WHERE id = @id", new { id }, tx)
            ?? throw new NotFoundException($"Order {id} not found.");

        var old = order.Status;
        order.ChangeStatus(newStatus);

        var changed = await conn.ExecuteAsync(
            "UPDATE orders SET status = @newStatus WHERE id = @id AND status = @old",
            new { id, newStatus = newStatus.ToString(), old = old.ToString() }, tx);
        if (changed == 0) throw new DomainException($"Order {id} was changed by someone else. Reload and try again.");

        await audit.RecordAsync(conn, tx, new AuditChange(
            "Order", id, AuditAction.OrderStatusChanged,
            $"Status: {old} → {newStatus}",
            [new FieldChange("Status", old.ToString(), newStatus.ToString())]));

        await tx.CommitAsync();
    }
}
