using System.Globalization;
using Dapper;
using FinGuitar.Web.Data;
using FinGuitar.Web.Domain;

namespace FinGuitar.Web.Services;

public class ProductService(Db db, CurrentUser user, AuditService audit)
{
    public static readonly string[] Categories = ["Electric", "Acoustic", "Bass", "Classical"];

    private const string Columns = "id, name, brand, category, price_cents, stock, image_file";

    public async Task<PagedResult<Product>> SearchAsync(ProductFilter filter, int page, int pageSize)
    {
        var where = new List<string>();
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            where.Add("(name LIKE @text ESCAPE '\\' OR brand LIKE @text ESCAPE '\\' OR category LIKE @text ESCAPE '\\')");
            p.Add("text", Sql.Contains(filter.Text));
        }
        if (filter.LowStockOnly)
        {
            where.Add("stock <= @lowStock");
            p.Add("lowStock", Product.LowStockThreshold);
        }
        var clause = where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where);

        p.Add("limit", pageSize);
        p.Add("offset", Sql.Offset(page, pageSize));

        await using var conn = await db.OpenAsync();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM products {clause}", p);
        var rows = (await conn.QueryAsync<Product>(
            $"SELECT {Columns} FROM products {clause} ORDER BY name, id LIMIT @limit OFFSET @offset", p)).ToList();
        return new PagedResult<Product>(rows, total, Math.Max(1, page), pageSize);
    }

    public async Task<Product?> GetAsync(int id)
    {
        await using var conn = await db.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<Product>($"SELECT {Columns} FROM products WHERE id = @id", new { id });
    }

    public async Task<IReadOnlyList<Product>> GetManyAsync(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        await using var conn = await db.OpenAsync();
        return (await conn.QueryAsync<Product>($"SELECT {Columns} FROM products WHERE id IN @list", new { list })).ToList();
    }

    /// <summary>Level1 only. New products get the placeholder image (photo upload is out of scope).</summary>
    public async Task<int> CreateAsync(ProductInput input)
    {
        user.RequireLevel1();
        Validate(input);

        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var id = await conn.ExecuteScalarAsync<int>(
            @"INSERT INTO products (name, brand, category, price_cents, stock, image_file)
              VALUES (@Name, @Brand, @Category, @cents, @Stock, NULL);
              SELECT last_insert_rowid();",
            new { input.Name, input.Brand, input.Category, cents = Money.ToCents(input.Price), input.Stock }, tx);

        await audit.RecordAsync(conn, tx, new AuditChange(
            "Product", id, AuditAction.ProductCreated,
            $"Created product {input.Name}",
            [
                new FieldChange("Name", null, input.Name),
                new FieldChange("Brand", null, input.Brand),
                new FieldChange("Category", null, input.Category),
                new FieldChange("Price", null, Fmt(input.Price)),
                new FieldChange("Stock", null, input.Stock.ToString(CultureInfo.InvariantCulture)),
            ]));

        await tx.CommitAsync();
        return id;
    }

    /// <summary>Level1 only. At least one value must change (FR-D2).</summary>
    public async Task UpdateAsync(int id, decimal? price, int? stock)
    {
        user.RequireLevel1();
        if (price is null && stock is null)
            throw new ValidationException(new Dictionary<string, string> { ["Price"] = "Enter a new price or a new stock." });

        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var current = await conn.QuerySingleOrDefaultAsync<Product>(
            $"SELECT {Columns} FROM products WHERE id = @id", new { id }, tx)
            ?? throw new NotFoundException($"Product {id} not found.");

        var newPrice = price ?? current.Price;
        var newStock = stock ?? current.Stock;
        ProductRules.Validate(newPrice, newStock);

        if (Money.ToCents(newPrice) == current.PriceCents && newStock == current.Stock)
            throw new DomainException($"No change: Product {id} already has that price and stock.");

        var changes = new List<FieldChange>();
        if (Money.ToCents(newPrice) != current.PriceCents)
            changes.Add(new FieldChange("Price", Fmt(current.Price), Fmt(newPrice)));
        if (newStock != current.Stock)
            changes.Add(new FieldChange("Stock", current.Stock.ToString(CultureInfo.InvariantCulture), newStock.ToString(CultureInfo.InvariantCulture)));

        await conn.ExecuteAsync(
            "UPDATE products SET price_cents = @cents, stock = @newStock WHERE id = @id",
            new { id, cents = Money.ToCents(newPrice), newStock }, tx);

        await audit.RecordAsync(conn, tx, new AuditChange(
            "Product", id, AuditAction.ProductUpdated,
            string.Join("; ", changes.Select(c => $"{c.Field}: {c.Old} → {c.New}")),
            changes));

        await tx.CommitAsync();
    }

    private static void Validate(ProductInput input)
    {
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(input.Name)) errors["Name"] = "Name is required.";
        if (string.IsNullOrWhiteSpace(input.Brand)) errors["Brand"] = "Brand is required.";
        if (!Categories.Contains(input.Category)) errors["Category"] = "Choose Electric, Acoustic, Bass, or Classical.";
        try { ProductRules.Validate(input.Price, input.Stock); }
        catch (ValidationException ex) { foreach (var (k, v) in ex.Errors) errors[k] = v; }
        if (errors.Count > 0) throw new ValidationException(errors);
    }

    private static string Fmt(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
