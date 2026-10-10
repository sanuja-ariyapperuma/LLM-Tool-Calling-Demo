using Dapper;
using FinGuitar.Web.Data;

namespace FinGuitar.Web.Services;

public record SalesSummary(DateTime From, DateTime To, int OrderCount, decimal Revenue);

public class TopProduct
{
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public int Units { get; set; }
    public long RevenueCents { get; set; }
    public decimal Revenue => Money.FromCents(RevenueCents);
}

public record DashboardData(
    SalesSummary Today,
    SalesSummary Week,
    int PendingCount,
    IReadOnlyList<TopProduct> TopProductsThisWeek);

/// <summary>Revenue (D7): orders that are not Cancelled, by order date. Both queries share one fragment.</summary>
public class SalesService(Db db, TimeProvider time)
{
    private const string NotCancelled = "o.status <> 'Cancelled'";

    public async Task<SalesSummary> GetSummaryAsync(DateTime from, DateTime to)
    {
        await using var conn = await db.OpenAsync();
        var row = await conn.QuerySingleAsync<(int Count, long Cents)>(
            $@"SELECT COUNT(*) AS Count, COALESCE(SUM(o.total_cents), 0) AS Cents FROM orders o
               WHERE {NotCancelled} AND o.order_date >= @from AND o.order_date < @toExclusive",
            new { from = from.Date, toExclusive = to.Date.AddDays(1) });
        return new SalesSummary(from.Date, to.Date, row.Count, Money.FromCents(row.Cents));
    }

    public async Task<IReadOnlyList<TopProduct>> GetTopProductsAsync(DateTime from, DateTime to, int count)
    {
        await using var conn = await db.OpenAsync();
        return (await conn.QueryAsync<TopProduct>(
            $@"SELECT p.id AS product_id, p.name, SUM(l.quantity) AS units, SUM(l.quantity * l.unit_price_cents) AS revenue_cents
               FROM order_lines l
               JOIN orders o ON o.id = l.order_id
               JOIN products p ON p.id = l.product_id
               WHERE {NotCancelled} AND o.order_date >= @from AND o.order_date < @toExclusive
               GROUP BY p.id, p.name
               ORDER BY units DESC, revenue_cents DESC, p.id
               LIMIT @count",
            new { from = from.Date, toExclusive = to.Date.AddDays(1), count })).ToList();
    }

    /// <summary>"This week" runs from Monday 00:00 to now, in server local time (D7).</summary>
    public static DateTime StartOfWeek(DateTime today) => today.Date.AddDays(-(((int)today.DayOfWeek + 6) % 7));

    public async Task<DashboardData> GetDashboardAsync()
    {
        var today = time.GetLocalNow().DateTime.Date;
        var monday = StartOfWeek(today);

        await using var conn = await db.OpenAsync();
        var pending = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM orders WHERE status = 'Pending'");

        return new DashboardData(
            await GetSummaryAsync(today, today),
            await GetSummaryAsync(monday, today),
            pending,
            await GetTopProductsAsync(monday, today, 5));
    }
}
