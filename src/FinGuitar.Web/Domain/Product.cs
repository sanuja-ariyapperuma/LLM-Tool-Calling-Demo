namespace FinGuitar.Web.Domain;

public class Product
{
    public const int LowStockThreshold = 3;

    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Category { get; set; } = "";
    public long PriceCents { get; set; }
    public decimal Price => Data.Money.FromCents(PriceCents);
    public int Stock { get; set; }
    public string? ImageFile { get; set; }

    public bool IsLowStock => Stock <= LowStockThreshold;
    public string ImageUrl => "/images/products/" + (string.IsNullOrWhiteSpace(ImageFile) ? "placeholder.jpg" : ImageFile);
}

public static class ProductRules
{
    public const decimal MaxPrice = 100_000m;
    public const int MaxStock = 10_000;

    /// <summary>FR-D2 ranges. Throws ValidationException with one message per bad field.</summary>
    public static void Validate(decimal price, int stock)
    {
        var errors = new Dictionary<string, string>();
        if (price <= 0 || price > MaxPrice)
            errors["Price"] = "Price must be greater than 0 and at most 100,000.";
        if (stock < 0 || stock > MaxStock)
            errors["Stock"] = "Stock must be between 0 and 10,000.";
        if (errors.Count > 0) throw new ValidationException(errors);
    }
}

public class ProductInput
{
    public string Name { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Price { get; set; }
    public int Stock { get; set; }
}
