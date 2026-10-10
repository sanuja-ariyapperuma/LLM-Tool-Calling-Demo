using System.Text.Json;
using FinGuitar.Web.Chat;
using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;
using Microsoft.Extensions.AI;

namespace FinGuitar.Tests;

public class ChatToolsTests
{
    private static string Json(object o) => JsonSerializer.Serialize(o);
    private static string? ErrorOf(object o) => JsonSerializer.SerializeToElement(o).TryGetProperty("error", out var e) ? e.GetString() : null;

    [Fact]
    public async Task The_catalog_gives_Level2_only_read_tools_and_Level1_all_nine()
    {
        await using var l1 = await ChatTestEnv.CreateAsync();
        await using var l2 = await ChatTestEnv.CreateAsync(TestEnv.Level2Id);

        var names1 = l1.Catalog.BuildFor(Roles.Level1).Select(t => t.Name).ToList();
        var names2 = l2.Catalog.BuildFor(Roles.Level2).Select(t => t.Name).ToList();

        Assert.Equal(9, names1.Count);
        Assert.Equal(7, names2.Count);
        Assert.DoesNotContain(names2, n => n.StartsWith("propose_"));
        Assert.Contains("propose_order_status_change", names1);
        Assert.Contains("propose_product_update", names1);
        Assert.All(l1.Catalog.Entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Description)));
        Assert.All(l1.Catalog.BuildFor(Roles.Level1), t => Assert.IsAssignableFrom<AIFunction>(t));
    }

    [Fact]
    public async Task Level2_calling_a_propose_tool_directly_gets_an_error_and_nothing_is_created()
    {
        await using var c = await ChatTestEnv.CreateAsync(TestEnv.Level2Id);

        var a = await c.Tools.ProposeOrderStatusChange(42, "Dispatched");
        var b = await c.Tools.ProposeProductUpdate(7, newStock: "12");

        Assert.Contains("permission", ErrorOf(a));
        Assert.Contains("permission", ErrorOf(b));
        Assert.False(c.Log.ProposalMade);
        Assert.Equal(0, await c.Scalar<int>("SELECT COUNT(*) FROM proposed_actions"));
    }

    [Fact]
    public async Task Level2_can_use_the_read_tools()
    {
        await using var c = await ChatTestEnv.CreateAsync(TestEnv.Level2Id);
        var r = await c.Tools.SearchOrders(status: "pending");
        Assert.Null(ErrorOf(r));
        Assert.NotNull(c.Log.Choose());
    }

    [Fact]
    public async Task Unknown_ids_come_back_as_error_results_not_exceptions()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        Assert.Equal("Order 999 not found.", ErrorOf(await c.Tools.GetOrderDetails(999)));
        Assert.Equal("Order 999 not found.", ErrorOf(await c.Tools.ProposeOrderStatusChange(999, "Dispatched")));
        Assert.Equal("Product 999 not found.", ErrorOf(await c.Tools.ProposeProductUpdate(999, newStock: "3")));
        Assert.Equal("Customer 999 not found.", ErrorOf(await c.Tools.GetCustomerDetails(999)));
        Assert.Null(c.Log.Choose()); // errors leave no payload to draw
    }

    [Fact]
    public async Task An_invalid_status_lists_the_valid_values_and_arguments_are_validated()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        Assert.Contains("Pending, Dispatched, Delivered, Cancelled", ErrorOf(await c.Tools.SearchOrders(status: "shipped")));
        Assert.Contains("Pending, Dispatched, Delivered, Cancelled", ErrorOf(await c.Tools.ProposeOrderStatusChange(42, "shipped")));
        Assert.Contains("yyyy-MM-dd", ErrorOf(await c.Tools.SearchOrders(dateFrom: "last week")));
        Assert.Contains("number", ErrorOf(await c.Tools.SearchOrders(minTotal: "lots")));
        Assert.Contains("whole number", ErrorOf(await c.Tools.ProposeProductUpdate(7, newStock: "many")));
        Assert.Contains("required", ErrorOf(await c.Tools.GetSalesSummary("", "")));
        Assert.Contains("before", ErrorOf(await c.Tools.GetSalesSummary("2026-10-09", "2026-10-01")));
    }

    [Fact]
    public async Task Omitted_arguments_arrive_as_empty_strings_and_mean_not_provided()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var r = JsonSerializer.SerializeToElement(await c.Tools.SearchOrders("", "", "", "", "", "", "", ""));
        Assert.True(r.GetProperty("totalCount").GetInt32() >= 45);
        Assert.Equal(20, r.GetProperty("showing").GetInt32());
    }

    [Fact]
    public async Task Search_returns_at_most_20_rows_says_how_many_matched_and_records_the_payload()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var result = JsonSerializer.SerializeToElement(await c.Tools.SearchOrders());
        Assert.True(result.GetProperty("totalCount").GetInt32() > 20);
        Assert.Equal(20, result.GetProperty("orders").GetArrayLength());

        var payload = c.Log.Choose()!;
        Assert.Equal(PayloadKinds.OrderTable, payload.Kind);
        var table = (OrderTablePayload)payload.Data;
        Assert.Equal(20, table.Rows.Count);
        Assert.True(table.TotalCount > 20);
    }

    [Fact]
    public async Task Finding_Johns_Les_Paul_order_by_description_gives_one_match()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var result = JsonSerializer.SerializeToElement(await c.Tools.SearchOrders(customerName: "John", productName: "Les Paul"));
        Assert.Equal(1, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(30, result.GetProperty("orders")[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task The_payload_rule_prefers_the_last_successful_data_result_and_a_proposal_over_both()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        await c.Tools.SearchOrders(status: "Pending");
        await c.Tools.GetOrderDetails(42);
        Assert.Equal(PayloadKinds.OrderDetail, c.Log.Choose()!.Kind);

        await c.Tools.GetOrderDetails(999); // an error does not replace it
        Assert.Equal(PayloadKinds.OrderDetail, c.Log.Choose()!.Kind);

        await c.Tools.ProposeOrderStatusChange(42, "Dispatched");
        Assert.Equal(PayloadKinds.ProposalCard, c.Log.Choose()!.Kind);
    }

    [Fact]
    public async Task Only_one_change_per_turn()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var first = await c.Tools.ProposeOrderStatusChange(42, "Dispatched");
        Assert.Null(ErrorOf(first));

        var second = await c.Tools.ProposeOrderStatusChange(44, "Dispatched");
        var third = await c.Tools.ProposeProductUpdate(7, newStock: "12");
        Assert.Equal("One change at a time. Ask the user to confirm this one first.", ErrorOf(second));
        Assert.Equal("One change at a time. Ask the user to confirm this one first.", ErrorOf(third));
        Assert.Equal(1, await c.Scalar<int>("SELECT COUNT(*) FROM proposed_actions"));
    }

    [Fact]
    public async Task Proposals_are_checked_against_the_rules_when_made_and_create_no_card_when_refused()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var delivered = await c.Scalar<int>("SELECT id FROM orders WHERE status = 'Delivered' LIMIT 1");

        Assert.Equal($"Order {delivered} is Delivered and cannot be changed to Pending.", ErrorOf(await c.Tools.ProposeOrderStatusChange(delivered, "Pending")));
        Assert.Contains("No change", ErrorOf(await c.Tools.ProposeProductUpdate(7, newPrice: "849", newStock: "4")));
        Assert.Contains("Price must be greater than 0", ErrorOf(await c.Tools.ProposeProductUpdate(7, newPrice: "0")));
        Assert.Contains("Stock must be between", ErrorOf(await c.Tools.ProposeProductUpdate(7, newStock: "10001")));
        Assert.Contains("price", ErrorOf(await c.Tools.ProposeProductUpdate(7))!.ToLower());

        Assert.False(c.Log.ProposalMade);
        Assert.Equal(0, await c.Scalar<int>("SELECT COUNT(*) FROM proposed_actions"));
    }

    [Fact]
    public async Task A_proposal_never_changes_data()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        await c.Tools.ProposeOrderStatusChange(42, "Dispatched");
        await c.Tools.ProposeProductUpdate(7, newStock: "12"); // refused (second), but even a single one must not write
        Assert.Equal("Pending", await c.Scalar<string>("SELECT status FROM orders WHERE id = 42"));
        Assert.Equal(0, await c.Scalar<int>("SELECT COUNT(*) FROM audit_entries"));
    }

    [Fact]
    public async Task Sales_tools_use_the_same_numbers_as_the_dashboard()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var today = c.Env.Db.Time.GetLocalNow().DateTime.Date;
        var monday = SalesService.StartOfWeek(today);
        var dash = await c.Env.Sales.GetDashboardAsync();

        var r = JsonSerializer.SerializeToElement(await c.Tools.GetSalesSummary(monday.ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd")));
        Assert.Equal(dash.Week.Revenue, r.GetProperty("revenue").GetDecimal());

        var top = JsonSerializer.SerializeToElement(await c.Tools.GetTopProducts(monday.ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd"), "3"));
        Assert.Equal(3, top.GetProperty("products").GetArrayLength());
        Assert.Equal(dash.TopProductsThisWeek[0].ProductId, top.GetProperty("products")[0].GetProperty("ProductId").GetInt32());
    }

    [Fact]
    public async Task Product_and_customer_tools_return_payloads()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var low = JsonSerializer.SerializeToElement(await c.Tools.SearchProducts(lowStockOnly: true));
        Assert.True(low.GetProperty("totalCount").GetInt32() >= 3);
        Assert.Equal(PayloadKinds.ProductTable, c.Log.Choose()!.Kind);

        await c.Tools.SearchCustomers("john");
        Assert.Equal(PayloadKinds.CustomerTable, c.Log.Choose()!.Kind);
        await c.Tools.GetCustomerDetails(1);
        Assert.Equal(PayloadKinds.CustomerDetail, c.Log.Choose()!.Kind);
    }

    [Fact]
    public void Payloads_round_trip_through_json_and_summarise_for_the_history()
    {
        var table = new OrderTablePayload([new OrderTableRow(12, "A", "2026-10-09", "Pending", 10m, "X"), new OrderTableRow(15, "B", "2026-10-08", "Pending", 5m, "Y")], 37);
        var json = ChatPayloadJson.Serialize(table);
        Assert.Equal(table.Rows[1], ((OrderTablePayload)ChatPayloadJson.Deserialize(PayloadKinds.OrderTable, json)!).Rows[1]);
        Assert.Equal("[Shown: orders 12, 15 (of 37)]", ChatPayloadJson.Summarise(PayloadKinds.OrderTable, json));
        Assert.Null(ChatPayloadJson.Summarise(null, null));
    }
}
