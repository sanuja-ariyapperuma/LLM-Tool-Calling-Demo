using System.Text.Json;
using FinGuitar.Web.Chat;
using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;

namespace FinGuitar.Tests;

public class CriticalActionPolicyTests
{
    private static ProductUpdateArgs Price(decimal now, decimal next) => new(1, "Test", now, 5, next, null);
    private static ProductUpdateArgs Stock(int next) => new(1, "Test", 100m, 5, null, next);

    [Theory]
    [InlineData(100, 119.9, false)]
    [InlineData(100, 120, true)]
    [InlineData(100, 80.1, false)]
    [InlineData(100, 80, true)]
    [InlineData(100, 125, true)]
    [InlineData(1000, 1050, false)]
    public void Price_changes_of_20_percent_or_more_are_critical_up_and_down(double now, double next, bool critical) =>
        Assert.Equal(critical, CriticalActionPolicy.Evaluate(Price((decimal)now, (decimal)next)).IsCritical);

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(50, false)]
    public void Setting_stock_to_zero_is_critical(int next, bool critical) =>
        Assert.Equal(critical, CriticalActionPolicy.Evaluate(Stock(next)).IsCritical);

    [Theory]
    [InlineData(OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Dispatched, false)]
    [InlineData(OrderStatus.Delivered, false)]
    public void Only_cancelling_an_order_is_critical(OrderStatus to, bool critical)
    {
        var v = CriticalActionPolicy.Evaluate(new OrderStatusArgs(42, OrderStatus.Pending, to));
        Assert.Equal(critical, v.IsCritical);
        Assert.Equal(critical, v.Warning is not null);
    }

    [Fact]
    public void Cancelling_names_the_order_in_the_warning_and_buttons()
    {
        var v = CriticalActionPolicy.Evaluate(new OrderStatusArgs(42, OrderStatus.Pending, OrderStatus.Cancelled));
        Assert.Contains("order 42 cannot be undone", v.Warning);
        Assert.Equal("Yes, cancel order 42", v.DangerLabel);
        Assert.Equal("Keep order", v.KeepLabel);
    }
}

public class ProposedActionLifecycleTests
{
    private static async Task<ProposedAction> ProposeDispatch42(ChatTestEnv c) =>
        await c.Actions.ProposeAsync(c.ConversationId, c.RequestMessageId, ProposedActionType.OrderStatusChange,
            new OrderStatusArgs(42, OrderStatus.Pending, OrderStatus.Dispatched), "Change order 42 from Pending to Dispatched");

    private static async Task<ProposedAction> ProposeCancel(ChatTestEnv c, int orderId = 43) =>
        await c.Actions.ProposeAsync(c.ConversationId, c.RequestMessageId, ProposedActionType.OrderStatusChange,
            new OrderStatusArgs(orderId, OrderStatus.Pending, OrderStatus.Cancelled), $"Change order {orderId} from Pending to Cancelled");

    private static Task<string> OrderStatusOf(ChatTestEnv c, int id) => c.Scalar<string>("SELECT status FROM orders WHERE id = @id", new { id });
    private static Task<int> AuditCount(ChatTestEnv c) => c.Scalar<int>("SELECT COUNT(*) FROM audit_entries");

    [Fact]
    public async Task A_non_critical_action_applies_on_the_first_confirm_through_the_normal_service()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var a = await ProposeDispatch42(c);
        Assert.False(a.IsCritical);
        Assert.Equal("Pending", await OrderStatusOf(c, 42)); // nothing has changed yet

        var outcome = await c.Actions.ConfirmAsync(a.Id);

        Assert.Equal(ProposedActionStatus.Confirmed, outcome.Action.Status);
        Assert.Equal("Dispatched", await OrderStatusOf(c, 42));
        var msg = Assert.Single(outcome.NewMessages);
        Assert.Equal(MessageRole.ActionResult, msg.Role);
        Assert.Equal("Order 42 is now Dispatched.", msg.Text);

        // The audit entry says Chat, and links back to the conversation, request, proposal, and one confirmation.
        var entry = Assert.Single((await c.Env.Audit.SearchAsync(new AuditFilter(), 1, 10)).Rows);
        Assert.Equal(ChangeSource.Chat, entry.Source);
        Assert.Equal(c.ConversationId, entry.ConversationId);
        Assert.Equal(c.RequestMessageId, entry.ChatMessageId);
        Assert.Equal(a.Id.ToString(), entry.ProposedActionId);
        Assert.Equal("test request", entry.ChatRequestText);
        Assert.Equal(1, entry.ConfirmationCount);
    }

    [Fact]
    public async Task Only_the_owner_can_confirm_and_other_users_get_not_found()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var a = await ProposeDispatch42(c);
        var other = ChatTestEnv.Another(c, TestEnv.Level2Id);

        await Assert.ThrowsAsync<NotFoundException>(() => other.Actions.ConfirmAsync(a.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => other.Actions.FinalConfirmAsync(a.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => other.Actions.CancelAsync(a.Id));
        Assert.Equal("Pending", await OrderStatusOf(c, 42));
    }

    [Fact]
    public async Task Level2_cannot_confirm_even_their_own_card()
    {
        await using var c = await ChatTestEnv.CreateAsync(TestEnv.Level2Id);
        var a = await ProposeDispatch42(c); // a direct service call: the tools would have refused
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Actions.ConfirmAsync(a.Id));
        Assert.Equal("Pending", await OrderStatusOf(c, 42));
        Assert.Equal(0, await AuditCount(c));
    }

    [Fact]
    public async Task A_card_works_only_once()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var a = await ProposeDispatch42(c);
        await c.Actions.ConfirmAsync(a.Id);

        var second = await c.Actions.ConfirmAsync(a.Id);
        Assert.NotNull(second.Notice);
        Assert.Empty(second.NewMessages);
        Assert.Equal(1, await AuditCount(c));
    }

    [Fact]
    public async Task A_card_expires_after_ten_minutes()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var a = await ProposeDispatch42(c);
        c.Env.Db.Time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));

        Assert.Equal(ProposedActionStatus.Expired, a.EffectiveStatus(c.Store.Now));
        var outcome = await c.Actions.ConfirmAsync(a.Id);
        Assert.Equal("Expired. Ask again.", outcome.Notice);
        Assert.Equal(ProposedActionStatus.Expired, outcome.Action.Status);
        Assert.Equal("Pending", await OrderStatusOf(c, 42));
    }

    [Fact]
    public async Task A_newer_card_in_the_same_conversation_expires_the_older_one_but_other_conversations_stay_live()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var first = await ProposeDispatch42(c);
        var firstConversation = c.ConversationId;

        await c.NewTurnAsync("another conversation");
        var elsewhere = await c.Actions.ProposeAsync(c.ConversationId, c.RequestMessageId, ProposedActionType.OrderStatusChange,
            new OrderStatusArgs(44, OrderStatus.Pending, OrderStatus.Dispatched), "Change order 44");

        await c.NewTurnAsync("back in the first", firstConversation);
        var newer = await c.Actions.ProposeAsync(c.ConversationId, c.RequestMessageId, ProposedActionType.OrderStatusChange,
            new OrderStatusArgs(42, OrderStatus.Pending, OrderStatus.Dispatched), "Change order 42 again");

        Assert.Equal(ProposedActionStatus.Expired, (await c.Actions.GetForViewAsync(first.Id))!.Status);
        Assert.Equal(ProposedActionStatus.Pending, (await c.Actions.GetForViewAsync(newer.Id))!.Status);
        Assert.Equal(ProposedActionStatus.Pending, (await c.Actions.GetForViewAsync(elsewhere.Id))!.Status);
    }

    [Fact]
    public async Task Sending_a_message_expires_open_cards_in_that_conversation_only()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var a = await ProposeDispatch42(c);
        var finalCard = await ProposeCancel(c);
        var conversation = c.ConversationId;

        await using (var conn = await c.Env.Db.Db.OpenAsync())
            await c.Actions.ExpireForConversationAsync(conn, null, conversation);

        Assert.Equal(ProposedActionStatus.Expired, (await c.Actions.GetForViewAsync(a.Id))!.Status);
        Assert.Equal(ProposedActionStatus.Expired, (await c.Actions.GetForViewAsync(finalCard.Id))!.Status);
    }

    [Fact]
    public async Task A_rule_failure_at_confirm_marks_the_card_Failed_and_changes_nothing()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var a = await ProposeDispatch42(c);
        await c.Exec("UPDATE orders SET status = 'Delivered' WHERE id = 42"); // the data moved on after the card was shown

        var outcome = await c.Actions.ConfirmAsync(a.Id);

        Assert.Equal(ProposedActionStatus.Failed, outcome.Action.Status);
        Assert.Contains("Order 42 is Delivered and cannot be changed to Dispatched.", outcome.Action.ResultText);
        Assert.True(outcome.NewMessages[0].IsError);
        Assert.Equal("Delivered", await OrderStatusOf(c, 42));
        Assert.Equal(0, await AuditCount(c));
    }

    [Fact]
    public async Task A_stale_product_card_is_rejected_and_a_fresh_one_applies()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var json = await c.Tools.ProposeProductUpdate(7, newStock: "12");
        Assert.NotNull(c.Log.ProposalId);
        await c.Exec("UPDATE products SET stock = 9 WHERE id = 7");

        var outcome = await c.Actions.ConfirmAsync(c.Log.ProposalId!.Value);
        Assert.Equal(ProposedActionStatus.Failed, outcome.Action.Status);
        Assert.Contains("changed since this was proposed", outcome.Action.ResultText);
        Assert.Equal(9, await c.Scalar<int>("SELECT stock FROM products WHERE id = 7"));
        Assert.NotNull(json);

        // Ask again against the current value: applies, and the audit entry says Chat.
        var again = await ChatTestEnv.CreateAsync();
        await using (again)
        {
            await again.Tools.ProposeProductUpdate(7, newStock: "12");
            var ok = await again.Actions.ConfirmAsync(again.Log.ProposalId!.Value);
            Assert.Equal(ProposedActionStatus.Confirmed, ok.Action.Status);
            Assert.Equal(12, await again.Scalar<int>("SELECT stock FROM products WHERE id = 7"));
            Assert.Contains("stock 4 → 12", ok.NewMessages[0].Text);
        }
    }

    [Fact]
    public async Task Cancel_marks_the_card_cancelled_and_changes_nothing()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var a = await ProposeDispatch42(c);
        var outcome = await c.Actions.CancelAsync(a.Id);
        Assert.Equal(ProposedActionStatus.Cancelled, outcome.Action.Status);
        Assert.Equal("Cancelled. Nothing was changed.", outcome.NewMessages[0].Text);
        Assert.Equal("Pending", await OrderStatusOf(c, 42));

        var again = await c.Actions.CancelAsync(a.Id);
        Assert.NotNull(again.Notice);
    }

    // ---- critical actions ---------------------------------------------------------------------------------------

    [Fact]
    public async Task The_first_confirm_of_a_critical_action_changes_no_data_and_posts_the_final_card()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var a = await ProposeCancel(c);
        Assert.True(a.IsCritical);

        var first = await c.Actions.ConfirmAsync(a.Id);

        Assert.Equal(ProposedActionStatus.AwaitingFinalConfirm, first.Action.Status);
        Assert.Equal("Pending", await OrderStatusOf(c, 43));
        Assert.Equal(0, await AuditCount(c));
        var card = Assert.Single(first.NewMessages);
        Assert.Equal(MessageRole.Assistant, card.Role);
        Assert.Equal(PayloadKinds.FinalConfirmCard, card.PayloadKind);
        var payload = (FinalConfirmCardPayload)ChatPayloadJson.Deserialize(card.PayloadKind, card.PayloadJson)!;
        Assert.Equal("Yes, cancel order 43", payload.DangerLabel);
        Assert.Contains("cannot be undone", payload.Warning);
    }

    [Fact]
    public async Task FinalConfirm_on_a_pending_action_is_refused()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var a = await ProposeCancel(c);

        var outcome = await c.Actions.FinalConfirmAsync(a.Id);

        Assert.NotNull(outcome.Notice);
        Assert.Equal(ProposedActionStatus.Pending, outcome.Action.Status);
        Assert.Equal("Pending", await OrderStatusOf(c, 43));
        Assert.Equal(0, await AuditCount(c));
    }

    [Fact]
    public async Task Both_confirms_apply_the_change_once_with_confirmation_count_2()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var a = await ProposeCancel(c);
        await c.Actions.ConfirmAsync(a.Id);

        var done = await c.Actions.FinalConfirmAsync(a.Id);
        Assert.Equal(ProposedActionStatus.Confirmed, done.Action.Status);
        Assert.Equal("Cancelled", await OrderStatusOf(c, 43));
        Assert.Equal("Order 43 is now Cancelled.", done.NewMessages[0].Text);

        var entry = Assert.Single((await c.Env.Audit.SearchAsync(new AuditFilter(), 1, 10)).Rows);
        Assert.Equal(2, entry.ConfirmationCount);
        Assert.Equal(ChangeSource.Chat, entry.Source);

        var second = await c.Actions.FinalConfirmAsync(a.Id);
        Assert.NotNull(second.Notice);
        Assert.Equal(1, await AuditCount(c));
    }

    [Fact]
    public async Task Keep_cancels_the_final_card_and_writes_no_audit_entry()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var a = await ProposeCancel(c, 44);
        await c.Actions.ConfirmAsync(a.Id);

        var keep = await c.Actions.CancelAsync(a.Id);

        Assert.Equal(ProposedActionStatus.Cancelled, keep.Action.Status);
        Assert.Equal("Pending", await OrderStatusOf(c, 44));
        Assert.Equal(0, await AuditCount(c));
    }

    [Fact]
    public async Task A_message_expires_an_action_waiting_for_its_final_confirm()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var a = await ProposeCancel(c);
        await c.Actions.ConfirmAsync(a.Id);

        await using (var conn = await c.Env.Db.Db.OpenAsync())
            await c.Actions.ExpireForConversationAsync(conn, null, c.ConversationId);

        var outcome = await c.Actions.FinalConfirmAsync(a.Id);
        Assert.Equal("Expired. Ask again.", outcome.Notice);
        Assert.Equal("Pending", await OrderStatusOf(c, 43));
    }

    [Fact]
    public async Task A_price_change_of_25_percent_needs_two_confirms_and_5_percent_needs_one()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        await c.Tools.ProposeProductUpdate(3, newPrice: "1250"); // product 3 costs 1000
        var big = await c.Actions.GetForViewAsync(c.Log.ProposalId!.Value);
        Assert.True(big!.IsCritical);

        var small = await ChatTestEnv.CreateAsync();
        await using (small)
        {
            await small.Tools.ProposeProductUpdate(3, newPrice: "1050");
            var action = await small.Actions.GetForViewAsync(small.Log.ProposalId!.Value);
            Assert.False(action!.IsCritical);
            var done = await small.Actions.ConfirmAsync(action.Id);
            Assert.Equal(ProposedActionStatus.Confirmed, done.Action.Status);
        }
    }

    [Fact]
    public void Arguments_are_stored_as_json_the_confirm_step_can_read()
    {
        var args = new ProductUpdateArgs(7, "Strat", 849m, 4, null, 12);
        var back = JsonSerializer.Deserialize<ProductUpdateArgs>(JsonSerializer.Serialize(args, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(args, back);
    }
}
