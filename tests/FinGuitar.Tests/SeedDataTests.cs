using Dapper;
using FinGuitar.Web.Data;
using FinGuitar.Web.Domain;

namespace FinGuitar.Tests;

public static class RepoPaths
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FinGuitar.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
        }
    }

    public static string Images => Path.Combine(Root, "src", "FinGuitar.Web", "wwwroot", "images", "products");
}

public class SeedDataTests
{
    [Fact]
    public async Task Seeding_an_empty_database_creates_the_expected_data()
    {
        await using var t = await TestDb.CreateAsync(seed: true);
        await using var conn = await t.Db.OpenAsync();

        Assert.Equal(15, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products"));
        Assert.Equal(10, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM customers"));
        Assert.True(await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM orders") >= 45);

        var users = (await conn.QueryAsync<(string Email, string Role)>("SELECT email, role FROM admin_users")).ToList();
        Assert.Contains(("level1@finguitar.local", "Level1"), users);
        Assert.Contains(("level2@finguitar.local", "Level2"), users);
        Assert.DoesNotContain(await conn.QueryAsync<string>("SELECT password_hash FROM admin_users"), h => h.Contains("Demo@"));

        // SD-1
        Assert.True(await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products WHERE stock <= 3") >= 3);
        Assert.True(await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products WHERE stock = 0") >= 1);
        Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products WHERE name LIKE '%Les Paul%'"));
        Assert.True(await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products WHERE name LIKE '%Stratocaster%'") >= 1);
        // SD-2
        Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM customers WHERE name LIKE '%John%'"));
        // Order totals match their lines
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM orders o WHERE o.total_cents <>
              (SELECT SUM(quantity * unit_price_cents) FROM order_lines l WHERE l.order_id = o.id)"));
    }

    [Fact]
    public async Task Seeding_twice_does_not_duplicate_anything()
    {
        await using var t = await TestDb.CreateAsync(seed: true);
        await new SeedData(t.Db, t.Time).SeedIfEmptyAsync();
        await using var conn = await t.Db.OpenAsync();

        Assert.Equal(15, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products"));
        Assert.Equal(2, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM admin_users"));
    }

    [Fact]
    public async Task Seeding_is_not_audited()
    {
        await using var t = await TestDb.CreateAsync(seed: true);
        await using var conn = await t.Db.OpenAsync();
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit_entries"));
    }

    [Fact]
    public async Task SD3_facts_hold_on_the_first_day()
    {
        await using var t = await TestDb.CreateAsync(seed: true);
        await using var conn = await t.Db.OpenAsync();
        var today = t.Time.GetLocalNow().DateTime.Date;

        Assert.True(await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM orders WHERE order_date >= @from AND order_date < @to",
            new { from = today, to = today.AddDays(1) }) >= 3);

        foreach (var id in new[] { 42, 43, 44 })
            Assert.Equal("Pending", await conn.ExecuteScalarAsync<string>("SELECT status FROM orders WHERE id = @id", new { id }));

        foreach (var status in Enum.GetValues<OrderStatus>())
            Assert.True(await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM orders WHERE status = @s", new { s = status.ToString() }) >= 1, status.ToString());

        var oldest = await conn.ExecuteScalarAsync<DateTime>("SELECT MIN(order_date) FROM orders");
        Assert.InRange((today - oldest.Date).Days, 25, 30);
    }

    [Theory]
    [InlineData(2026, 10, 5)]
    [InlineData(2026, 10, 6)]
    [InlineData(2026, 10, 7)]
    [InlineData(2026, 10, 8)]
    [InlineData(2026, 10, 9)]
    [InlineData(2026, 10, 10)]
    [InlineData(2026, 10, 11)]
    public async Task Johns_only_Les_Paul_order_is_in_the_previous_Monday_based_week(int y, int m, int d)
    {
        var start = new DateTime(y, m, d, 12, 0, 0);
        await using var t = await TestDb.CreateAsync(start, seed: true);
        await using var conn = await t.Db.OpenAsync();

        var dates = (await conn.QueryAsync<DateTime>(
            @"SELECT o.order_date FROM orders o
              JOIN customers c ON c.id = o.customer_id
              WHERE c.name LIKE '%John%'
                AND EXISTS (SELECT 1 FROM order_lines l JOIN products p ON p.id = l.product_id
                            WHERE l.order_id = o.id AND p.name LIKE '%Les Paul%')")).ToList();

        var date = Assert.Single(dates);
        var thisMonday = start.Date.AddDays(-(((int)start.DayOfWeek + 6) % 7));
        Assert.Equal(DayOfWeek.Wednesday, date.DayOfWeek);
        Assert.True(date.Date >= thisMonday.AddDays(-7) && date.Date < thisMonday);
    }

    [Fact]
    public async Task Every_seeded_photo_exists_and_is_credited()
    {
        await using var t = await TestDb.CreateAsync(seed: true);
        await using var conn = await t.Db.OpenAsync();
        var files = (await conn.QueryAsync<string>("SELECT image_file FROM products")).ToList();
        var credits = await File.ReadAllTextAsync(Path.Combine(RepoPaths.Images, "CREDITS.md"));

        Assert.True(File.Exists(Path.Combine(RepoPaths.Images, "placeholder.jpg")), "placeholder.jpg");
        foreach (var f in files)
        {
            Assert.True(File.Exists(Path.Combine(RepoPaths.Images, f)), $"{f} missing on disk");
            Assert.Contains(f, credits);
        }
    }
}
