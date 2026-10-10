using System.Text.Json;
using FinGuitar.Web.Domain;

namespace FinGuitar.Web.Services;

public record CartLine(Product Product, int Quantity)
{
    public decimal LineTotal => Product.Price * Quantity;
}

public record CartView(IReadOnlyList<CartLine> Lines)
{
    public decimal Total => Lines.Sum(l => l.LineTotal);
    public int ItemCount => Lines.Sum(l => l.Quantity);
}

/// <summary>The session cart: productId → quantity, stored as JSON. Prices and stock are always read fresh from the database.</summary>
public class CartService(IHttpContextAccessor accessor, ProductService products)
{
    private const string SessionKey = "cart";

    private ISession Session => accessor.HttpContext!.Session;

    public async Task<CartView> GetAsync()
    {
        var items = Load();
        var found = (await products.GetManyAsync(items.Keys)).ToDictionary(p => p.Id);

        var lines = new List<CartLine>();
        foreach (var (productId, quantity) in items)
        {
            // A product that vanished or sold out since it was added simply drops out of the cart.
            if (!found.TryGetValue(productId, out var product) || product.Stock <= 0) continue;
            lines.Add(new CartLine(product, Math.Clamp(quantity, 1, product.Stock)));
        }
        return new CartView(lines);
    }

    public async Task AddAsync(int productId, int quantity = 1)
    {
        var product = await products.GetAsync(productId) ?? throw new NotFoundException($"Product {productId} not found.");
        if (product.Stock <= 0) throw new DomainException($"{product.Name} is out of stock.");

        var items = Load();
        items.TryGetValue(productId, out var existing);
        items[productId] = Math.Clamp(existing + Math.Max(1, quantity), 1, product.Stock);
        Save(items);
    }

    public async Task UpdateAsync(int productId, int quantity)
    {
        var items = Load();
        if (!items.ContainsKey(productId)) return;

        var product = await products.GetAsync(productId);
        if (product is null || product.Stock <= 0) { items.Remove(productId); }
        else items[productId] = Math.Clamp(quantity, 1, product.Stock);
        Save(items);
    }

    public void Remove(int productId)
    {
        var items = Load();
        if (items.Remove(productId)) Save(items);
    }

    public int Count() => Load().Values.Sum();

    private Dictionary<int, int> Load()
    {
        var json = Session.GetString(SessionKey);
        return string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<Dictionary<int, int>>(json) ?? [];
    }

    private void Save(Dictionary<int, int> items) => Session.SetString(SessionKey, JsonSerializer.Serialize(items));
}
