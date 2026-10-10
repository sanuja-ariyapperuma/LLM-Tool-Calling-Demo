using Dapper;
using FinGuitar.Web.Data;
using FinGuitar.Web.Domain;

namespace FinGuitar.Web.Services;

public class CustomerRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public int OrderCount { get; set; }
}

public record CustomerDetails(Customer Customer, IReadOnlyList<OrderRow> Orders);

public class CustomerService(Db db)
{
    public async Task<PagedResult<CustomerRow>> SearchAsync(string? text, int page, int pageSize)
    {
        var p = new DynamicParameters();
        var clause = "";
        if (!string.IsNullOrWhiteSpace(text))
        {
            clause = "WHERE (c.name LIKE @text ESCAPE '\\' OR c.email LIKE @text ESCAPE '\\' OR c.phone LIKE @text ESCAPE '\\')";
            p.Add("text", Sql.Contains(text));
        }
        p.Add("limit", pageSize);
        p.Add("offset", Sql.Offset(page, pageSize));

        await using var conn = await db.OpenAsync();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM customers c {clause}", p);
        var rows = (await conn.QueryAsync<CustomerRow>(
            $@"SELECT c.id, c.name, c.email, c.phone, (SELECT COUNT(*) FROM orders o WHERE o.customer_id = c.id) AS order_count
               FROM customers c {clause} ORDER BY c.name, c.id LIMIT @limit OFFSET @offset", p)).ToList();
        return new PagedResult<CustomerRow>(rows, total, Math.Max(1, page), pageSize);
    }

    public async Task<CustomerDetails?> GetDetailsAsync(int id)
    {
        await using var conn = await db.OpenAsync();
        var customer = await conn.QuerySingleOrDefaultAsync<Customer>(
            "SELECT id, name, email, phone, address FROM customers WHERE id = @id", new { id });
        if (customer is null) return null;

        var orders = (await conn.QueryAsync<OrderRow>(
            @"SELECT o.id, o.customer_id, c.name AS customer_name, o.order_date, o.status, o.total_cents,
                COALESCE((SELECT group_concat(p.name, ', ') FROM order_lines l JOIN products p ON p.id = l.product_id WHERE l.order_id = o.id), '') AS items
              FROM orders o JOIN customers c ON c.id = o.customer_id
              WHERE o.customer_id = @id ORDER BY o.order_date DESC, o.id DESC", new { id })).ToList();
        return new CustomerDetails(customer, orders);
    }
}
