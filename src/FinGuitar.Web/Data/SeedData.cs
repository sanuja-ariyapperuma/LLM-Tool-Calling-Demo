using Dapper;
using FinGuitar.Web.Domain;
using Microsoft.AspNetCore.Identity;

namespace FinGuitar.Web.Data;

/// <summary>
/// Fills an empty database (D10). Dates are relative to the first start (D19): orders 42 to 45 are dated today,
/// and John's only Les Paul order is on the Wednesday of the previous Monday-based week.
/// Seeding is not audited.
/// </summary>
public sealed class SeedData(Db db, TimeProvider time)
{
    public const string Level1Password = "Demo@Level1";
    public const string Level2Password = "Demo@Level2";

    private record SeedProduct(string Name, string Brand, string Category, decimal Price, int Stock, string Image);

    // Product ids follow list order (1..15). Product 7 is the Stratocaster, product 3 is priced at a round 1000.
    private static readonly SeedProduct[] Products =
    [
        new("Fender Player Telecaster", "Fender", "Electric", 849m, 12, "fender-player-telecaster.jpg"),
        new("Gibson Les Paul Standard", "Gibson", "Electric", 2499m, 6, "gibson-les-paul-standard.jpg"),
        new("Ibanez RG Standard", "Ibanez", "Electric", 1000m, 9, "ibanez-rg-standard.jpg"),
        new("PRS SE Custom 24", "PRS", "Electric", 899m, 8, "prs-se-custom-24.jpg"),
        new("Yamaha Pacifica 112V", "Yamaha", "Electric", 349m, 15, "yamaha-pacifica-112v.jpg"),
        new("Fender Player Jazz Bass", "Fender", "Bass", 899m, 7, "fender-player-jazz-bass.jpg"),
        new("Fender Player Stratocaster", "Fender", "Electric", 849m, 4, "fender-player-stratocaster.jpg"),
        new("Martin D-28", "Martin", "Acoustic", 3299m, 2, "martin-d-28.jpg"),
        new("Taylor 214ce", "Taylor", "Acoustic", 1299m, 3, "taylor-214ce.jpg"),
        new("Yamaha FG800", "Yamaha", "Acoustic", 229m, 20, "yamaha-fg800.jpg"),
        new("Gibson SG Standard", "Gibson", "Electric", 1699m, 0, "gibson-sg-standard.jpg"),
        new("Yamaha C40 Classical", "Yamaha", "Classical", 149m, 25, "yamaha-c40-classical.jpg"),
        new("Ibanez SR300E Bass", "Ibanez", "Bass", 449m, 1, "ibanez-sr300e-bass.jpg"),
        new("Martin LX1 Little Martin", "Martin", "Acoustic", 399m, 10, "martin-lx1-little-martin.jpg"),
        new("Taylor GS Mini", "Taylor", "Acoustic", 699m, 6, "taylor-gs-mini.jpg"),
    ];

    private static readonly (string Name, string Email, string Phone, string Address)[] Customers =
    [
        ("John Carter", "john.carter@example.com", "555-0101", "12 Maple Street, Springfield"),
        ("Maria Gonzalez", "maria.gonzalez@example.com", "555-0102", "48 Oak Avenue, Riverton"),
        ("Priya Nair", "priya.nair@example.com", "555-0103", "7 Lake Road, Fairview"),
        ("Liam O'Connor", "liam.oconnor@example.com", "555-0104", "93 Hill Lane, Ashford"),
        ("Sofia Rossi", "sofia.rossi@example.com", "555-0105", "5 Harbour View, Bayside"),
        ("Ahmed Hassan", "ahmed.hassan@example.com", "555-0106", "21 Station Road, Westbury"),
        ("Emma Wilson", "emma.wilson@example.com", "555-0107", "66 Garden Close, Eastwood"),
        ("Chen Wei", "chen.wei@example.com", "555-0108", "104 River Street, Northgate"),
        ("Olivia Brown", "olivia.brown@example.com", "555-0109", "30 Park Drive, Southfield"),
        ("Noah Peterson", "noah.peterson@example.com", "555-0110", "15 Mill Lane, Kingsley"),
    ];

    private const int JohnId = 1;
    private const int LesPaulId = 2;
    private const int JohnsLesPaulOrderId = 30;
    private const int OrderCount = 48;

    public async Task SeedIfEmptyAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        if (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products") > 0) return;

        var now = time.GetLocalNow().DateTime;
        await using var tx = await conn.BeginTransactionAsync(ct);

        foreach (var p in Products)
        {
            await conn.ExecuteAsync(
                "INSERT INTO products (name, brand, category, price_cents, stock, image_file) VALUES (@Name, @Brand, @Category, @cents, @Stock, @Image)",
                new { p.Name, p.Brand, p.Category, cents = Money.ToCents(p.Price), p.Stock, p.Image }, tx);
        }

        foreach (var c in Customers)
        {
            await conn.ExecuteAsync(
                "INSERT INTO customers (name, email, phone, address) VALUES (@Name, @Email, @Phone, @Address)",
                new { c.Name, c.Email, c.Phone, c.Address }, tx);
        }

        for (var id = 1; id <= OrderCount; id++)
        {
            var (customerId, date, productIds) = PlanOrder(id, now);
            var status = StatusFor(id, (now.Date - date.Date).Days);

            long total = 0;
            var lines = new List<(int ProductId, int Quantity, long Cents)>();
            foreach (var productId in productIds)
            {
                var quantity = id % 5 == 0 ? 2 : 1;
                var cents = Money.ToCents(Products[productId - 1].Price);
                lines.Add((productId, quantity, cents));
                total += cents * quantity;
            }

            await conn.ExecuteAsync(
                "INSERT INTO orders (id, customer_id, order_date, status, total_cents) VALUES (@id, @customerId, @date, @status, @total)",
                new { id, customerId, date, status = status.ToString(), total }, tx);
            foreach (var l in lines)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO order_lines (order_id, product_id, quantity, unit_price_cents) VALUES (@id, @ProductId, @Quantity, @Cents)",
                    new { id, l.ProductId, l.Quantity, l.Cents }, tx);
            }
        }

        var hasher = new PasswordHasher<AdminUser>();
        foreach (var (email, role, password) in new[]
                 {
                     ("level1@finguitar.local", Roles.Level1, Level1Password),
                     ("level2@finguitar.local", Roles.Level2, Level2Password),
                 })
        {
            await conn.ExecuteAsync(
                "INSERT INTO admin_users (email, password_hash, role) VALUES (@email, @hash, @role)",
                new { email, hash = hasher.HashPassword(new AdminUser { Email = email }, password), role }, tx);
        }

        await tx.CommitAsync(ct);
    }

    /// <summary>The Wednesday of the previous Monday-based week (D19).</summary>
    public static DateTime WednesdayOfPreviousWeek(DateTime today)
    {
        var thisMonday = today.Date.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return thisMonday.AddDays(-7).AddDays(2);
    }

    private static (int CustomerId, DateTime Date, int[] ProductIds) PlanOrder(int id, DateTime now)
    {
        if (id == JohnsLesPaulOrderId)
            return (JohnId, WednesdayOfPreviousWeek(now).AddHours(14), [LesPaulId]);

        // 42 to 45 are today, 46 to 48 yesterday; the rest are spread over the last 30 days.
        var daysAgo = id switch
        {
            >= 42 and <= 45 => 0,
            >= 46 => 1,
            _ => 30 - (id - 1) * 28 / 40,
        };
        var date = now.Date.AddDays(-daysAgo).AddHours(8 + id % 9);
        if (date > now) date = now;
        date = new DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second);

        var customerId = id * 3 % 10 + 1;
        var first = id * 7 % 15 + 1;
        var second = (id * 4 + 3) % 15 + 1;
        var productIds = id % 3 == 0 && second != first ? new[] { first, second } : [first];

        // John's only Les Paul order is the special one above.
        if (customerId == JohnId)
            productIds = productIds.Select(p => p == LesPaulId ? 3 : p).Distinct().ToArray();
        return (customerId, date, productIds);
    }

    private static OrderStatus StatusFor(int id, int daysAgo)
    {
        if (id is 42 or 43 or 44) return OrderStatus.Pending;
        if (id == JohnsLesPaulOrderId) return OrderStatus.Delivered;
        if (daysAgo <= 1) return id % 2 == 0 ? OrderStatus.Pending : OrderStatus.Dispatched;
        if (daysAgo <= 4) return id % 4 == 0 ? OrderStatus.Pending : OrderStatus.Dispatched;
        return id % 7 == 0 ? OrderStatus.Cancelled : OrderStatus.Delivered;
    }
}
