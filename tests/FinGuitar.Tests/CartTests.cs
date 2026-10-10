using System.Net;
using Dapper;
using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;
using Microsoft.AspNetCore.Http;

namespace FinGuitar.Tests;

public class CartTests
{
    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _data = [];
        public bool IsAvailable => true;
        public string Id => "test";
        public IEnumerable<string> Keys => _data.Keys;
        public void Clear() => _data.Clear();
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Remove(string key) => _data.Remove(key);
        public void Set(string key, byte[] value) => _data[key] = value;
        public bool TryGetValue(string key, out byte[]? value) => _data.TryGetValue(key, out value);
    }

    private static (CartService Cart, TestEnv Env) Create(TestEnv env)
    {
        var accessor = new FixedAccessor { HttpContext = new DefaultHttpContext { Session = new FakeSession() } };
        return (new CartService(accessor, env.Products), env);
    }

    [Fact]
    public async Task Add_update_and_remove_with_line_and_grand_totals()
    {
        await using var e = await TestEnv.CreateAsync(userId: null);
        var (cart, _) = Create(e);

        await cart.AddAsync(1);          // 849.00
        await cart.AddAsync(1);          // same line, quantity 2
        await cart.AddAsync(5, 3);       // 349.00 x 3
        var view = await cart.GetAsync();
        Assert.Equal(2, view.Lines.Count);
        Assert.Equal(2, view.Lines.Single(l => l.Product.Id == 1).Quantity);
        Assert.Equal(849m * 2 + 349m * 3, view.Total);
        Assert.Equal(5, view.ItemCount);

        await cart.UpdateAsync(1, 1);
        Assert.Equal(1, (await cart.GetAsync()).Lines.Single(l => l.Product.Id == 1).Quantity);

        cart.Remove(5);
        var after = await cart.GetAsync();
        Assert.Single(after.Lines);
        Assert.Equal(849m, after.Total);
    }

    [Fact]
    public async Task Quantity_is_limited_to_1_through_stock()
    {
        await using var e = await TestEnv.CreateAsync(userId: null);
        var (cart, _) = Create(e);

        await cart.AddAsync(8, 50);                       // Martin D-28: stock 2
        Assert.Equal(2, (await cart.GetAsync()).Lines[0].Quantity);
        await cart.UpdateAsync(8, 0);
        Assert.Equal(1, (await cart.GetAsync()).Lines[0].Quantity);
        await cart.UpdateAsync(8, 99);
        Assert.Equal(2, (await cart.GetAsync()).Lines[0].Quantity);
    }

    [Fact]
    public async Task Out_of_stock_products_cannot_be_added()
    {
        await using var e = await TestEnv.CreateAsync(userId: null);
        var (cart, _) = Create(e);

        var ex = await Assert.ThrowsAsync<DomainException>(() => cart.AddAsync(11)); // Gibson SG: stock 0
        Assert.Contains("out of stock", ex.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => cart.AddAsync(999));
        Assert.Empty((await cart.GetAsync()).Lines);
    }

    [Fact]
    public async Task Prices_come_from_the_database_not_the_session()
    {
        await using var e = await TestEnv.CreateAsync(userId: null);
        var (cart, _) = Create(e);
        await cart.AddAsync(10);                          // Yamaha FG800, 229.00
        Assert.Equal(229m, (await cart.GetAsync()).Total);

        await using var conn = await e.Db.Db.OpenAsync();
        await conn.ExecuteAsync("UPDATE products SET price_cents = 25000 WHERE id = 10");
        Assert.Equal(250m, (await cart.GetAsync()).Total);

        await conn.ExecuteAsync("UPDATE products SET stock = 0 WHERE id = 10"); // sold out since it was added
        Assert.Empty((await cart.GetAsync()).Lines);
    }

    [Fact]
    public async Task Storefront_pages_render_and_add_to_cart_works_end_to_end()
    {
        using var app = new AppFactory();
        var client = app.NewClient();

        var home = await client.GetStringAsync("/");
        Assert.Contains("Les Paul", home);
        Assert.Contains("Out of stock", home);
        Assert.Contains("loading=\"lazy\"", home);

        var details = await client.GetStringAsync("/Products/Details/2");
        Assert.Contains("Add to cart", details);

        var token = await client.GetTokenAsync("/Products/Details/2");
        var res = await client.PostFormAsync("/Cart?handler=Add", token, ("productId", "2"));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        var cart = await client.GetStringAsync("/Cart");
        Assert.Contains("Gibson Les Paul Standard", cart);
        Assert.Contains("Checkout is not part of this demo", cart);
        Assert.Contains("Cart (1)", cart);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Products/Details/9999")).StatusCode);
    }
}
