using Dapper;
using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;

namespace FinGuitar.Tests;

public class OrderSearchTests
{
    [Fact]
    public async Task Each_filter_works_on_its_own()
    {
        await using var e = await TestEnv.CreateAsync();

        Assert.Equal(1, (await e.Orders.SearchAsync(new OrderFilter(OrderId: 42), 1, 25)).TotalCount);

        var pending = await e.Orders.SearchAsync(new OrderFilter(Status: OrderStatus.Pending), 1, 25);
        Assert.All(pending.Rows, r => Assert.Equal(OrderStatus.Pending, r.Status));
        Assert.Contains(pending.Rows, r => r.Id == 42);

        var john = await e.Orders.SearchAsync(new OrderFilter(CustomerName: "jOhN"), 1, 25);
        Assert.NotEmpty(john.Rows);
        Assert.All(john.Rows, r => Assert.Contains("John", r.CustomerName));

        var lp = await e.Orders.SearchAsync(new OrderFilter(ProductName: "les paul"), 1, 25);
        Assert.NotEmpty(lp.Rows);
        Assert.All(lp.Rows, r => Assert.Contains("Les Paul", r.Items));

        var cheap = await e.Orders.SearchAsync(new OrderFilter(MaxTotal: 300), 1, 25);
        Assert.All(cheap.Rows, r => Assert.True(r.Total <= 300));
        var dear = await e.Orders.SearchAsync(new OrderFilter(MinTotal: 2000), 1, 25);
        Assert.All(dear.Rows, r => Assert.True(r.Total >= 2000));
    }

    [Fact]
    public async Task Filters_combine_and_John_has_exactly_one_Les_Paul_order()
    {
        await using var e = await TestEnv.CreateAsync();
        var r = await e.Orders.SearchAsync(new OrderFilter(CustomerName: "John", ProductName: "Les Paul"), 1, 20);
        Assert.Equal(1, r.TotalCount);
        Assert.Equal(30, r.Rows[0].Id);
    }

    [Fact]
    public async Task DateTo_includes_the_whole_day_and_DateFrom_starts_at_midnight()
    {
        await using var e = await TestEnv.CreateAsync();
        var today = e.Db.Time.GetLocalNow().DateTime.Date;

        var todays = await e.Orders.SearchAsync(new OrderFilter(DateFrom: today, DateTo: today), 1, 25);
        Assert.True(todays.TotalCount >= 3);
        Assert.All(todays.Rows, r => Assert.Equal(today, r.OrderDate.Date));

        // An order late on the last day must still be found when DateTo is that day.
        await using var conn = await e.Db.Db.OpenAsync();
        await conn.ExecuteAsync("UPDATE orders SET order_date = @d WHERE id = 1", new { d = today.AddHours(23).AddMinutes(59) });
        var late = await e.Orders.SearchAsync(new OrderFilter(OrderId: 1, DateTo: today), 1, 25);
        Assert.Equal(1, late.TotalCount);
    }

    [Fact]
    public async Task Paging_uses_the_same_method_for_25_and_20_and_reports_the_total()
    {
        await using var e = await TestEnv.CreateAsync();
        var page = await e.Orders.SearchAsync(new OrderFilter(), 1, 25);
        var tool = await e.Orders.SearchAsync(new OrderFilter(), 1, 20);
        Assert.Equal(25, page.Rows.Count);
        Assert.Equal(20, tool.Rows.Count);
        Assert.Equal(page.TotalCount, tool.TotalCount);
        Assert.True(page.TotalCount >= 45);

        var second = await e.Orders.SearchAsync(new OrderFilter(), 2, 25);
        Assert.DoesNotContain(second.Rows, r => page.Rows.Any(p => p.Id == r.Id));
        // newest first
        Assert.True(page.Rows[0].OrderDate >= page.Rows[^1].OrderDate);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    public async Task Like_wildcards_in_input_are_matched_literally(string term)
    {
        await using var e = await TestEnv.CreateAsync();
        Assert.Equal(0, (await e.Orders.SearchAsync(new OrderFilter(CustomerName: term), 1, 25)).TotalCount);
        Assert.Equal(0, (await e.Orders.SearchAsync(new OrderFilter(ProductName: term), 1, 25)).TotalCount);
    }
}

public class ProductSearchTests
{
    [Fact]
    public async Task Text_matches_name_brand_or_category_case_insensitively()
    {
        await using var e = await TestEnv.CreateAsync();
        Assert.Single((await e.Products.SearchAsync(new ProductFilter("les paul"), 1, 20)).Rows);
        Assert.All((await e.Products.SearchAsync(new ProductFilter("yamaha"), 1, 20)).Rows, p => Assert.Equal("Yamaha", p.Brand));
        Assert.All((await e.Products.SearchAsync(new ProductFilter("bass"), 1, 20)).Rows, p => Assert.Contains("Bass", p.Name + p.Category));
    }

    [Fact]
    public async Task Low_stock_filter_returns_stock_of_3_or_less()
    {
        await using var e = await TestEnv.CreateAsync();
        var low = await e.Products.SearchAsync(new ProductFilter(LowStockOnly: true), 1, 20);
        Assert.True(low.TotalCount >= 3);
        Assert.All(low.Rows, p => Assert.True(p.Stock <= 3));
        Assert.Contains(low.Rows, p => p.Stock == 0);
    }

    [Fact]
    public async Task Paging_reports_the_total_count()
    {
        await using var e = await TestEnv.CreateAsync();
        var p = await e.Products.SearchAsync(new ProductFilter(), 1, 10);
        Assert.Equal(10, p.Rows.Count);
        Assert.Equal(15, p.TotalCount);
        Assert.Equal(2, p.TotalPages);
    }

    [Fact]
    public async Task Customers_search_by_name_email_or_phone()
    {
        await using var e = await TestEnv.CreateAsync();
        Assert.Single((await e.Customers.SearchAsync("john", 1, 20)).Rows);
        Assert.Single((await e.Customers.SearchAsync("maria.gonzalez@", 1, 20)).Rows);
        Assert.Single((await e.Customers.SearchAsync("555-0103", 1, 20)).Rows);
        var details = await e.Customers.GetDetailsAsync(1);
        Assert.NotNull(details);
        Assert.NotEmpty(details!.Orders);
        Assert.Null(await e.Customers.GetDetailsAsync(999));
    }
}
