using System.Net;

namespace FinGuitar.Tests;

public class AdminPageTests
{
    [Fact]
    public async Task Anonymous_visitors_are_sent_to_the_login_page()
    {
        using var app = new AppFactory();
        var client = app.NewClient();

        foreach (var url in new[] { "/Admin", "/Admin/Orders", "/Admin/Audit", "/Admin/Products/Create" })
        {
            var res = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
            Assert.StartsWith("/Account/Login", res.Headers.Location!.OriginalString.Replace("http://localhost", ""));
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode); // storefront stays public
    }

    [Fact]
    public async Task Sign_in_shows_the_dashboard_and_Logout_rejects_GET()
    {
        using var app = new AppFactory();
        var client = app.NewClient();
        await client.LoginLevel1Async();

        var dash = await client.GetStringAsync("/Admin");
        Assert.Contains("Dashboard", dash);
        Assert.Contains("Level1 · full access", WebUtility.HtmlDecode(dash));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync("/Account/Logout")).StatusCode);

        var token = await client.GetTokenAsync("/Admin");
        var res = await client.PostFormAsync("/Account/Logout", token);
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Admin")).StatusCode); // signed out
    }

    [Fact]
    public async Task Wrong_password_shows_the_same_message()
    {
        using var app = new AppFactory();
        var client = app.NewClient();
        var token = await client.GetTokenAsync("/Account/Login");
        var res = await client.PostFormAsync("/Account/Login", token, ("Email", "level1@finguitar.local"), ("Password", "wrong"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("Invalid email or password.", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cancel_button_shows_a_panel_and_changes_nothing_then_Yes_cancels_with_one_audit_entry()
    {
        using var app = new AppFactory();
        var client = app.NewClient();
        await client.LoginLevel1Async();

        var panel = await client.GetStringAsync("/Admin/Orders/Details/43?confirm=cancel");
        Assert.Contains("Yes, cancel order 43", panel);
        Assert.Contains("Keep order", panel);
        Assert.Equal("Pending", await app.QueryAsync<string>("SELECT status FROM orders WHERE id = 43"));
        Assert.Equal(0, await app.QueryAsync<int>("SELECT COUNT(*) FROM audit_entries"));

        var token = await client.GetTokenAsync("/Admin/Orders/Details/43?confirm=cancel");
        var res = await client.PostFormAsync("/Admin/Orders/Details/43?handler=Status", token, ("newStatus", "Cancelled"));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        Assert.Equal("Cancelled", await app.QueryAsync<string>("SELECT status FROM orders WHERE id = 43"));
        Assert.Equal(1, await app.QueryAsync<int>("SELECT COUNT(*) FROM audit_entries"));
        Assert.Equal("AdminPanel", await app.QueryAsync<string>("SELECT source FROM audit_entries"));
    }

    [Fact]
    public async Task Status_buttons_offer_only_allowed_changes()
    {
        using var app = new AppFactory();
        var client = app.NewClient();
        await client.LoginLevel1Async();

        var pending = await client.GetStringAsync("/Admin/Orders/Details/42");
        Assert.Contains("Mark as Dispatched", pending);
        Assert.Contains("Cancel order", pending);
        Assert.DoesNotContain("Mark as Delivered", pending);

        var delivered = await app.QueryAsync<int>("SELECT id FROM orders WHERE status = 'Delivered' LIMIT 1");
        var page = await client.GetStringAsync($"/Admin/Orders/Details/{delivered}");
        Assert.Contains("cannot be changed", page);
        Assert.DoesNotContain("Mark as", page);
    }

    [Fact]
    public async Task Level2_sees_no_buttons_and_a_direct_POST_gets_403_and_changes_nothing()
    {
        using var app = new AppFactory();
        var client = app.NewClient();
        await client.LoginLevel2Async();

        var page = await client.GetStringAsync("/Admin/Orders/Details/42");
        Assert.DoesNotContain("Mark as Dispatched", page);
        Assert.Contains("read only", page);

        var token = await client.GetTokenAsync("/Admin"); // the Sign out form carries a valid token
        var res = await client.PostFormAsync("/Admin/Orders/Details/42?handler=Status", token, ("newStatus", "Dispatched"));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("Pending", await app.QueryAsync<string>("SELECT status FROM orders WHERE id = 42"));
        Assert.Equal(0, await app.QueryAsync<int>("SELECT COUNT(*) FROM audit_entries"));

        // Level2 is also kept away from the product forms.
        var edit = await client.GetAsync("/Admin/Products/Edit/7");
        Assert.True(edit.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_POST_without_an_anti_forgery_token_is_refused()
    {
        using var app = new AppFactory();
        var client = app.NewClient();
        await client.LoginLevel1Async();

        var res = await client.PostAsync("/Admin/Orders/Details/42?handler=Status",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("newStatus", "Dispatched")]));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Pending", await app.QueryAsync<string>("SELECT status FROM orders WHERE id = 42"));
    }

    [Fact]
    public async Task Product_edit_writes_an_audit_entry_and_shows_in_history_and_on_the_audit_page()
    {
        using var app = new AppFactory();
        var client = app.NewClient();
        await client.LoginLevel1Async();

        var token = await client.GetTokenAsync("/Admin/Products/Edit/7");
        var res = await client.PostFormAsync("/Admin/Products/Edit/7", token, ("Input.Price", "899.00"), ("Input.Stock", "12"));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        var details = await client.GetStringAsync("/Admin/Products/Details/7");
        Assert.Contains("Price: 849.00 → 899.00", WebUtility.HtmlDecode(details));
        var audit = await client.GetStringAsync("/Admin/Audit?EntityType=Product&EntityId=7");
        Assert.Contains("Product 7", audit);

        // An invalid price is refused with the rule message and nothing changes.
        var bad = await client.PostFormAsync("/Admin/Products/Edit/7", await client.GetTokenAsync("/Admin/Products/Edit/7"),
            ("Input.Price", "0"), ("Input.Stock", "12"));
        Assert.Contains("Price must be greater than 0", await bad.Content.ReadAsStringAsync());
        Assert.Equal(1, await app.QueryAsync<int>("SELECT COUNT(*) FROM audit_entries"));
    }

    [Fact]
    public async Task Every_admin_list_page_renders_for_Level2()
    {
        using var app = new AppFactory();
        var client = app.NewClient();
        await client.LoginLevel2Async();

        foreach (var url in new[] { "/Admin", "/Admin/Orders", "/Admin/Orders?Status=Pending&Customer=john", "/Admin/Orders/Details/42",
                     "/Admin/Products", "/Admin/Products?LowStockOnly=true", "/Admin/Products/Details/7",
                     "/Admin/Customers", "/Admin/Customers/Details/1", "/Admin/Audit" })
        {
            var res = await client.GetAsync(url);
            Assert.True(res.StatusCode == HttpStatusCode.OK, $"{url} returned {res.StatusCode}");
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Admin/Orders/Details/9999")).StatusCode);
    }

    [Fact]
    public async Task Level1_can_create_a_product()
    {
        using var app = new AppFactory();
        var client = app.NewClient();
        await client.LoginLevel1Async();

        var token = await client.GetTokenAsync("/Admin/Products/Create");
        var res = await client.PostFormAsync("/Admin/Products/Create", token,
            ("Input.Name", "Test Telecaster"), ("Input.Brand", "Fender"), ("Input.Category", "Electric"), ("Input.Price", "700.00"), ("Input.Stock", "3"));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal(16, await app.QueryAsync<int>("SELECT COUNT(*) FROM products"));
        Assert.Equal("ProductCreated", await app.QueryAsync<string>("SELECT action FROM audit_entries"));
    }
}
