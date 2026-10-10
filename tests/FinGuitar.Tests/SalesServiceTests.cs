using Dapper;
using FinGuitar.Web.Services;

namespace FinGuitar.Tests;

public class SalesServiceTests
{
    private static async Task<decimal> ExpectedRevenue(TestEnv e, DateTime from, DateTime toInclusiveDay)
    {
        await using var conn = await e.Db.Db.OpenAsync();
        var cents = await conn.ExecuteScalarAsync<long>(
            "SELECT COALESCE(SUM(total_cents), 0) FROM orders WHERE status <> 'Cancelled' AND order_date >= @f AND order_date < @t",
            new { f = from.Date, t = toInclusiveDay.Date.AddDays(1) });
        return cents / 100m;
    }

    [Fact]
    public async Task Revenue_excludes_cancelled_orders()
    {
        await using var e = await TestEnv.CreateAsync();
        var all = e.Db.Time.GetLocalNow().DateTime.Date;
        var from = all.AddDays(-40);

        var summary = await e.Sales.GetSummaryAsync(from, all);
        Assert.Equal(await ExpectedRevenue(e, from, all), summary.Revenue);

        await using var conn = await e.Db.Db.OpenAsync();
        var cancelledCents = await conn.ExecuteScalarAsync<long>("SELECT SUM(total_cents) FROM orders WHERE status = 'Cancelled'");
        var allCents = await conn.ExecuteScalarAsync<long>("SELECT SUM(total_cents) FROM orders");
        Assert.True(cancelledCents > 0);
        Assert.Equal((allCents - cancelledCents) / 100m, summary.Revenue);
    }

    [Fact]
    public async Task Top_sellers_exclude_cancelled_orders()
    {
        await using var e = await TestEnv.CreateAsync();
        var today = e.Db.Time.GetLocalNow().DateTime.Date;
        await using var conn = await e.Db.Db.OpenAsync();
        // Cancel every order, then only an unrelated one carries weight.
        await conn.ExecuteAsync("UPDATE orders SET status = 'Cancelled'");
        await conn.ExecuteAsync("UPDATE orders SET status = 'Pending' WHERE id = 42");

        var top = await e.Sales.GetTopProductsAsync(today.AddDays(-40), today, 5);
        var units = await conn.ExecuteScalarAsync<int>("SELECT SUM(quantity) FROM order_lines WHERE order_id = 42");
        Assert.Equal(units, top.Sum(t => t.Units));
    }

    [Fact]
    public async Task Week_runs_from_Monday_and_orders_before_it_do_not_count()
    {
        // Friday 2026-10-09: Monday is 2026-10-05.
        await using var e = await TestEnv.CreateAsync(now: new DateTime(2026, 10, 9, 12, 0, 0));
        Assert.Equal(new DateTime(2026, 10, 5), SalesService.StartOfWeek(new DateTime(2026, 10, 9)));
        Assert.Equal(new DateTime(2026, 10, 5), SalesService.StartOfWeek(new DateTime(2026, 10, 11))); // Sunday

        await using var conn = await e.Db.Db.OpenAsync();
        await conn.ExecuteAsync("UPDATE orders SET status = 'Delivered', order_date = '2026-10-04 23:59:59' WHERE id = 1");
        await conn.ExecuteAsync("UPDATE orders SET status = 'Delivered', order_date = '2026-10-05 00:00:00' WHERE id = 2");
        var before = (await e.Sales.GetDashboardAsync()).Week;

        await conn.ExecuteAsync("UPDATE orders SET total_cents = total_cents + 100000 WHERE id = 1"); // before Monday
        var afterEarly = (await e.Sales.GetDashboardAsync()).Week;
        Assert.Equal(before.Revenue, afterEarly.Revenue);

        await conn.ExecuteAsync("UPDATE orders SET total_cents = total_cents + 100000 WHERE id = 2"); // on Monday 00:00
        var afterMonday = (await e.Sales.GetDashboardAsync()).Week;
        Assert.Equal(before.Revenue + 1000m, afterMonday.Revenue);
    }

    [Fact]
    public async Task Dashboard_equals_GetSummaryAsync_for_the_same_range()
    {
        await using var e = await TestEnv.CreateAsync();
        var today = e.Db.Time.GetLocalNow().DateTime.Date;
        var monday = SalesService.StartOfWeek(today);

        var dash = await e.Sales.GetDashboardAsync();
        Assert.Equal(await e.Sales.GetSummaryAsync(monday, today), dash.Week);
        Assert.Equal(await e.Sales.GetSummaryAsync(today, today), dash.Today);
        Assert.Equal(await e.Sales.GetTopProductsAsync(monday, today, 5), dash.TopProductsThisWeek, new TopComparer());
        Assert.True(dash.PendingCount >= 3);
    }

    private sealed class TopComparer : IEqualityComparer<TopProduct>
    {
        public bool Equals(TopProduct? a, TopProduct? b) => a!.ProductId == b!.ProductId && a.Units == b.Units && a.RevenueCents == b.RevenueCents;
        public int GetHashCode(TopProduct t) => t.ProductId;
    }
}
