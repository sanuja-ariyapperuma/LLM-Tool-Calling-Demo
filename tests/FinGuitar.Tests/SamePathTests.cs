using System.Text.Json;
using Dapper;
using FinGuitar.Web.Chat;
using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;

namespace FinGuitar.Tests;

/// <summary>
/// The point of the sample (D17, NFR-11): the chat calls the same service methods as the admin pages,
/// so the same change ends in the same state with the same audit entry, and the same mistakes give the same messages.
/// </summary>
public class SamePathTests
{
    private record Snapshot(string Status, string Summary, string ChangesJson, string UserEmail, string UserRole, string Action, string EntityType, int EntityId);

    private static async Task<Snapshot> Capture(TestEnv e, int orderId)
    {
        await using var conn = await e.Db.Db.OpenAsync();
        var status = await conn.ExecuteScalarAsync<string>("SELECT status FROM orders WHERE id = @orderId", new { orderId });
        var a = (await e.Audit.SearchAsync(new AuditFilter(), 1, 10)).Rows.Single();
        return new Snapshot(status!, a.Summary, a.ChangesJson, a.UserEmail, a.UserRole, a.Action.ToString(), a.EntityType, a.EntityId);
    }

    [Fact]
    public async Task Dispatching_order_42_from_the_page_and_from_the_chat_ends_the_same()
    {
        // Admin page path
        await using var page = await TestEnv.CreateAsync();
        await page.Orders.ChangeStatusAsync(42, OrderStatus.Dispatched);
        var fromPage = await Capture(page, 42);
        var pageEntry = (await page.Audit.SearchAsync(new AuditFilter(), 1, 10)).Rows.Single();

        // Chat path
        await using var chat = await ChatTestEnv.CreateAsync();
        await chat.Tools.ProposeOrderStatusChange(42, "dispatched");
        await chat.Actions.ConfirmAsync(chat.Log.ProposalId!.Value);
        var fromChat = await Capture(chat.Env, 42);
        var chatEntry = (await chat.Env.Audit.SearchAsync(new AuditFilter(), 1, 10)).Rows.Single();

        Assert.Equal(fromPage, fromChat);
        Assert.Equal(ChangeSource.AdminPanel, pageEntry.Source);
        Assert.Equal(ChangeSource.Chat, chatEntry.Source);
        Assert.Null(pageEntry.ConversationId);
        Assert.NotNull(chatEntry.ConversationId);
    }

    [Fact]
    public async Task Cancelling_from_the_page_and_from_the_chat_ends_the_same_and_the_chat_entry_says_confirmed_twice()
    {
        await using var page = await TestEnv.CreateAsync();
        await page.Orders.ChangeStatusAsync(43, OrderStatus.Cancelled);
        var fromPage = await Capture(page, 43);

        await using var chat = await ChatTestEnv.CreateAsync();
        await chat.Tools.ProposeOrderStatusChange(43, "Cancelled");
        var id = chat.Log.ProposalId!.Value;
        await chat.Actions.ConfirmAsync(id);
        await chat.Actions.FinalConfirmAsync(id);

        Assert.Equal(fromPage, await Capture(chat.Env, 43));
        Assert.Equal(2, (await chat.Env.Audit.SearchAsync(new AuditFilter(), 1, 10)).Rows.Single().ConfirmationCount);
    }

    [Fact]
    public async Task A_product_stock_change_from_the_page_and_from_the_chat_gives_the_same_audit_changes()
    {
        await using var page = await TestEnv.CreateAsync();
        await page.Products.UpdateAsync(7, null, 12);
        var pageEntry = (await page.Audit.SearchAsync(new AuditFilter(), 1, 10)).Rows.Single();

        await using var chat = await ChatTestEnv.CreateAsync();
        await chat.Tools.ProposeProductUpdate(7, newStock: "12");
        await chat.Actions.ConfirmAsync(chat.Log.ProposalId!.Value);
        var chatEntry = (await chat.Env.Audit.SearchAsync(new AuditFilter(), 1, 10)).Rows.Single();

        Assert.Equal(pageEntry.ChangesJson, chatEntry.ChangesJson);
        Assert.Equal(pageEntry.Summary, chatEntry.Summary);
        await using (var conn = await page.Db.Db.OpenAsync())
            Assert.Equal(12, await conn.ExecuteScalarAsync<int>("SELECT stock FROM products WHERE id = 7"));
        Assert.Equal(12, await chat.Scalar<int>("SELECT stock FROM products WHERE id = 7"));
    }

    [Fact]
    public async Task A_status_rule_violation_gives_the_same_message_from_the_service_and_the_chat()
    {
        await using var chat = await ChatTestEnv.CreateAsync();
        var delivered = await chat.Scalar<int>("SELECT id FROM orders WHERE status = 'Delivered' LIMIT 1");

        var serviceMessage = (await Assert.ThrowsAsync<DomainException>(() => chat.Env.Orders.ChangeStatusAsync(delivered, OrderStatus.Pending))).Message;
        var toolMessage = JsonSerializer.SerializeToElement(await chat.Tools.ProposeOrderStatusChange(delivered, "Pending")).GetProperty("error").GetString();

        Assert.Equal(serviceMessage, toolMessage);
    }

    [Theory]
    [InlineData("0", null)]
    [InlineData("100000.01", null)]
    [InlineData(null, "-1")]
    [InlineData(null, "10001")]
    public async Task A_validation_failure_gives_the_same_message_from_the_service_and_the_chat(string? price, string? stock)
    {
        await using var chat = await ChatTestEnv.CreateAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => chat.Env.Products.UpdateAsync(
            7, price is null ? null : decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture), stock is null ? null : int.Parse(stock)));
        var toolMessage = JsonSerializer.SerializeToElement(await chat.Tools.ProposeProductUpdate(7, price, stock)).GetProperty("error").GetString();

        Assert.Equal(string.Join(" ", ex.Errors.Values), toolMessage);
    }

    [Fact]
    public async Task A_no_change_gives_the_same_message_from_the_service_and_the_chat()
    {
        await using var chat = await ChatTestEnv.CreateAsync();
        var serviceMessage = (await Assert.ThrowsAsync<DomainException>(() => chat.Env.Products.UpdateAsync(7, 849m, 4))).Message;
        var toolMessage = JsonSerializer.SerializeToElement(await chat.Tools.ProposeProductUpdate(7, "849", "4")).GetProperty("error").GetString();
        Assert.Equal(serviceMessage, toolMessage);
    }
}
