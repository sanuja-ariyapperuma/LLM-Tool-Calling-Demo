using Dapper;
using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;

namespace FinGuitar.Tests;

public class RbacServiceTests
{
    private static async Task<int> AuditCount(TestEnv e)
    {
        await using var conn = await e.Db.Db.OpenAsync();
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit_entries");
    }

    [Fact]
    public async Task Level2_cannot_write_and_nothing_changes_or_is_audited()
    {
        await using var e = await TestEnv.CreateAsync(TestEnv.Level2Id);
        await using var conn = await e.Db.Db.OpenAsync();
        var status = await conn.ExecuteScalarAsync<string>("SELECT status FROM orders WHERE id = 42");
        var stock = await conn.ExecuteScalarAsync<int>("SELECT stock FROM products WHERE id = 7");
        var productCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products");

        await Assert.ThrowsAsync<ForbiddenException>(() => e.Orders.ChangeStatusAsync(42, OrderStatus.Dispatched));
        await Assert.ThrowsAsync<ForbiddenException>(() => e.Products.UpdateAsync(7, 100m, 12));
        await Assert.ThrowsAsync<ForbiddenException>(() => e.Products.CreateAsync(
            new ProductInput { Name = "X", Brand = "Y", Category = "Bass", Price = 10, Stock = 1 }));

        Assert.Equal(status, await conn.ExecuteScalarAsync<string>("SELECT status FROM orders WHERE id = 42"));
        Assert.Equal(stock, await conn.ExecuteScalarAsync<int>("SELECT stock FROM products WHERE id = 7"));
        Assert.Equal(productCount, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products"));
        Assert.Equal(0, await AuditCount(e));
    }

    [Fact]
    public async Task Anonymous_callers_cannot_write_either()
    {
        await using var e = await TestEnv.CreateAsync(userId: null);
        await Assert.ThrowsAsync<ForbiddenException>(() => e.Orders.ChangeStatusAsync(42, OrderStatus.Dispatched));
    }

    [Fact]
    public async Task Level2_can_read()
    {
        await using var e = await TestEnv.CreateAsync(TestEnv.Level2Id);
        Assert.NotEmpty((await e.Orders.SearchAsync(new OrderFilter(), 1, 5)).Rows);
        Assert.NotNull(await e.Orders.GetDetailsAsync(42));
        Assert.NotNull(await e.Products.GetAsync(7));
        Assert.True(e.User.IsAdmin);
        Assert.False(e.User.IsLevel1);
    }
}

public class AuditTests
{
    private static async Task<List<AuditEntry>> Entries(TestEnv e) => (await e.Audit.SearchAsync(new AuditFilter(), 1, 100)).Rows.ToList();

    [Fact]
    public async Task Admin_status_change_writes_one_entry_with_source_user_and_field_change()
    {
        await using var e = await TestEnv.CreateAsync();
        await e.Orders.ChangeStatusAsync(42, OrderStatus.Dispatched);

        var entry = Assert.Single(await Entries(e));
        Assert.Equal(ChangeSource.AdminPanel, entry.Source);
        Assert.Equal(TestEnv.Level1Id, entry.UserId);
        Assert.Equal("level1@finguitar.local", entry.UserEmail);
        Assert.Equal("Level1", entry.UserRole);
        Assert.Equal("Order", entry.EntityType);
        Assert.Equal(42, entry.EntityId);
        Assert.Equal(AuditAction.OrderStatusChanged, entry.Action);
        Assert.Equal("Status: Pending → Dispatched", entry.Summary);
        Assert.Equal([new FieldChange("Status", "Pending", "Dispatched")], AuditService.ParseChanges(entry.ChangesJson));
        Assert.Null(entry.ConversationId);
        Assert.Null(entry.ConfirmationCount);
    }

    [Fact]
    public async Task Product_create_and_update_each_write_one_entry()
    {
        await using var e = await TestEnv.CreateAsync();
        var id = await e.Products.CreateAsync(new ProductInput { Name = "Test Guitar", Brand = "Fender", Category = "Electric", Price = 500m, Stock = 4 });
        await e.Products.UpdateAsync(id, 550m, null);
        await e.Products.UpdateAsync(7, null, 12);

        var entries = await Entries(e);
        Assert.Equal(3, entries.Count);
        var created = entries.Single(x => x.Action == AuditAction.ProductCreated);
        Assert.Equal(id, created.EntityId);
        var priceEdit = entries.Single(x => x.Action == AuditAction.ProductUpdated && x.EntityId == id);
        Assert.Equal([new FieldChange("Price", "500.00", "550.00")], AuditService.ParseChanges(priceEdit.ChangesJson));
        var stockEdit = entries.Single(x => x.EntityId == 7);
        Assert.Equal([new FieldChange("Stock", "4", "12")], AuditService.ParseChanges(stockEdit.ChangesJson));
        Assert.All(entries, x => Assert.Equal(ChangeSource.AdminPanel, x.Source));
    }

    [Fact]
    public async Task Chat_origin_is_recorded_when_the_context_is_marked()
    {
        await using var e = await TestEnv.CreateAsync();
        e.Context.MarkAsChat(5, 17, "action-guid", "order 42 is dispatched", 2);
        await e.Orders.ChangeStatusAsync(42, OrderStatus.Dispatched);

        var entry = Assert.Single(await Entries(e));
        Assert.Equal(ChangeSource.Chat, entry.Source);
        Assert.Equal(5, entry.ConversationId);
        Assert.Equal(17, entry.ChatMessageId);
        Assert.Equal("action-guid", entry.ProposedActionId);
        Assert.Equal("order 42 is dispatched", entry.ChatRequestText);
        Assert.Equal(2, entry.ConfirmationCount);
    }

    [Fact]
    public async Task A_failed_rule_leaves_no_entry_and_no_change()
    {
        await using var e = await TestEnv.CreateAsync();
        await using var conn = await e.Db.Db.OpenAsync();
        var delivered = await conn.ExecuteScalarAsync<int>("SELECT id FROM orders WHERE status = 'Delivered' LIMIT 1");

        var ex = await Assert.ThrowsAsync<DomainException>(() => e.Orders.ChangeStatusAsync(delivered, OrderStatus.Pending));
        Assert.Equal($"Order {delivered} is Delivered and cannot be changed to Pending.", ex.Message);

        await Assert.ThrowsAsync<ValidationException>(() => e.Products.UpdateAsync(7, 0m, null));
        var noChange = await Assert.ThrowsAsync<DomainException>(() => e.Products.UpdateAsync(7, 849m, 4));
        Assert.Equal("No change: Product 7 already has that price and stock.", noChange.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => e.Orders.ChangeStatusAsync(999, OrderStatus.Dispatched));

        Assert.Empty(await Entries(e));
        Assert.Equal("Delivered", await conn.ExecuteScalarAsync<string>("SELECT status FROM orders WHERE id = @delivered", new { delivered }));
        Assert.Equal(84900, await conn.ExecuteScalarAsync<long>("SELECT price_cents FROM products WHERE id = 7"));
    }

    [Fact]
    public async Task Filters_and_paging_work()
    {
        await using var e = await TestEnv.CreateAsync();
        await e.Orders.ChangeStatusAsync(42, OrderStatus.Dispatched);
        await e.Products.UpdateAsync(7, null, 12);
        e.Context.MarkAsChat(1, 2, "g", "dispatch 44", 1);
        await e.Orders.ChangeStatusAsync(44, OrderStatus.Dispatched);

        Assert.Equal(3, (await e.Audit.SearchAsync(new AuditFilter(), 1, 50)).TotalCount);
        Assert.Equal(1, (await e.Audit.SearchAsync(new AuditFilter(Source: ChangeSource.Chat), 1, 50)).TotalCount);
        Assert.Equal(2, (await e.Audit.SearchAsync(new AuditFilter(EntityType: "Order"), 1, 50)).TotalCount);
        Assert.Equal(1, (await e.Audit.SearchAsync(new AuditFilter(EntityType: "Order", EntityId: 42), 1, 50)).TotalCount);
        Assert.Equal(3, (await e.Audit.SearchAsync(new AuditFilter(UserId: TestEnv.Level1Id), 1, 50)).TotalCount);
        Assert.Equal(0, (await e.Audit.SearchAsync(new AuditFilter(UserId: 99), 1, 50)).TotalCount);

        var today = e.Db.Time.GetLocalNow().DateTime.Date;
        Assert.Equal(3, (await e.Audit.SearchAsync(new AuditFilter(DateFrom: today, DateTo: today), 1, 50)).TotalCount);
        Assert.Equal(0, (await e.Audit.SearchAsync(new AuditFilter(DateTo: today.AddDays(-1)), 1, 50)).TotalCount);

        var paged = await e.Audit.SearchAsync(new AuditFilter(), 2, 2);
        Assert.Single(paged.Rows);
        Assert.Equal(2, paged.TotalPages);

        var users = await e.Audit.GetUsersAsync();
        Assert.Single(users);
        Assert.Single(await e.Audit.GetForEntityAsync("Order", 42, 5));
    }
}
