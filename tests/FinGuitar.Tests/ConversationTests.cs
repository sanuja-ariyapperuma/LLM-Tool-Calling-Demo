using System.Net;
using System.Text.RegularExpressions;

namespace FinGuitar.Tests;

public class ConversationTests
{
    private static readonly Regex ActionId = new(@"data-card-action=""Confirm"" data-id=""([0-9a-f-]{36})""", RegexOptions.Compiled);

    private static async Task<(string Html, int ConversationId, HttpResponseMessage Res)> SendAsync(HttpClient client, string text, int? conversationId = null)
    {
        var token = await client.GetTokenAsync("/Admin/Chat");
        var fields = new List<(string, string)> { ("text", text) };
        if (conversationId is { } id) fields.Add(("conversationId", id.ToString()));
        var res = await client.PostFormAsync("/Admin/Chat?handler=Send", token, fields.ToArray());
        var html = await res.Content.ReadAsStringAsync();
        var conv = res.Headers.TryGetValues("X-Conversation-Id", out var v) ? int.Parse(v.First()) : 0;
        return (html, conv, res);
    }

    [Fact]
    public async Task Sending_a_message_returns_the_server_drawn_table_and_reloading_shows_it_again()
    {
        using var app = new AppFactory();
        var client = app.NewClient();
        await client.LoginLevel1Async();

        var (html, conv, res) = await SendAsync(client, "what are the pending orders?");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(conv > 0);
        Assert.Contains("Done. See below.", html);
        Assert.Contains("data-table", html);
        Assert.Contains("/Admin/Orders/Details/42", html);

        var reloaded = await client.GetStringAsync($"/Admin/Chat?c={conv}");
        Assert.Contains("what are the pending orders?", reloaded);
        Assert.Contains("/Admin/Orders/Details/42", reloaded);
        Assert.Contains("aria-current=\"page\"", reloaded); // the sidebar marks the open conversation
    }

    [Fact]
    public async Task Other_users_cannot_see_or_use_a_conversation_and_get_404_everywhere()
    {
        using var app = new AppFactory();
        var owner = app.NewClient();
        await owner.LoginLevel1Async();
        var (html, conv, _) = await SendAsync(owner, "order 42 is dispatched");
        var actionId = ActionId.Match(html).Groups[1].Value;
        Assert.NotEmpty(actionId);

        var other = app.NewClient();
        await other.LoginLevel2Async();
        var token = await other.GetTokenAsync("/Admin/Chat");

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/Admin/Chat?c={conv}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostFormAsync("/Admin/Chat?handler=Send", token, ("text", "hi"), ("conversationId", conv.ToString()))).StatusCode);
        foreach (var handler in new[] { "Confirm", "FinalConfirm", "Cancel" })
            Assert.Equal(HttpStatusCode.NotFound, (await other.PostFormAsync($"/Admin/Chat?handler={handler}&id={actionId}", token)).StatusCode);

        Assert.Equal("Pending", await app.QueryAsync<string>("SELECT status FROM orders WHERE id = 42"));
        Assert.DoesNotContain("order 42 is dispatched", await other.GetStringAsync("/Admin/Chat"));
    }

    [Fact]
    public async Task Confirm_applies_the_change_and_the_audit_page_shows_the_Chat_badge_quote_and_owner_only_link()
    {
        using var app = new AppFactory();
        var l1 = app.NewClient();
        await l1.LoginLevel1Async();
        var (html, conv, _) = await SendAsync(l1, "order 42 is dispatched");
        Assert.Contains("Change order 42 from Pending to Dispatched", html);
        Assert.Contains("Use the buttons to confirm", html);
        Assert.Equal("Pending", await app.QueryAsync<string>("SELECT status FROM orders WHERE id = 42"));

        var id = ActionId.Match(html).Groups[1].Value;
        var token = await l1.GetTokenAsync("/Admin/Chat");
        var res = await l1.PostFormAsync($"/Admin/Chat?handler=Confirm&id={id}", token);
        var body = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("Order 42 is now Dispatched.", body);
        Assert.Contains("Confirmed", body);
        Assert.Equal("Dispatched", await app.QueryAsync<string>("SELECT status FROM orders WHERE id = 42"));

        // A price edit on the Products page adds an Admin panel entry (scenario 16).
        var editToken = await l1.GetTokenAsync("/Admin/Products/Edit/7");
        await l1.PostFormAsync("/Admin/Products/Edit/7", editToken, ("Input.Price", "899.00"), ("Input.Stock", "4"));

        var audit = await l1.GetStringAsync("/Admin/Audit");
        Assert.Contains("badge-chat", audit);
        Assert.Contains("order 42 is dispatched", audit);
        Assert.Contains($"/Admin/Chat?c={conv}#m", audit);
        Assert.Contains("Admin panel", audit);

        var onlyChat = await l1.GetStringAsync("/Admin/Audit?Source=Chat");
        Assert.Contains("Order 42", onlyChat);
        Assert.DoesNotContain("Product 7", onlyChat);

        // Level2 sees the quote but not the link.
        var l2 = app.NewClient();
        await l2.LoginLevel2Async();
        var l2Audit = await l2.GetStringAsync("/Admin/Audit");
        Assert.Contains("order 42 is dispatched", l2Audit);
        Assert.DoesNotContain("View in chat", l2Audit);
        Assert.Contains("View in chat", audit);
    }

    [Fact]
    public async Task A_critical_cancel_needs_two_confirms_and_the_audit_entry_says_confirmed_twice()
    {
        using var app = new AppFactory();
        var client = app.NewClient();
        await client.LoginLevel1Async();
        var (html, _, _) = await SendAsync(client, "cancel 43");
        Assert.Contains("Critical action", html);
        var id = ActionId.Match(html).Groups[1].Value;
        var token = await client.GetTokenAsync("/Admin/Chat");

        var first = await (await client.PostFormAsync($"/Admin/Chat?handler=Confirm&id={id}", token)).Content.ReadAsStringAsync();
        Assert.Contains("Yes, cancel order 43", first);
        Assert.Contains("Keep order", first);
        Assert.Contains("cannot be undone", first);
        Assert.Equal("Pending", await app.QueryAsync<string>("SELECT status FROM orders WHERE id = 43"));
        Assert.Equal(0, await app.QueryAsync<int>("SELECT COUNT(*) FROM audit_entries"));

        var second = await (await client.PostFormAsync($"/Admin/Chat?handler=FinalConfirm&id={id}", token)).Content.ReadAsStringAsync();
        Assert.Contains("Order 43 is now Cancelled.", second);
        Assert.Equal("Cancelled", await app.QueryAsync<string>("SELECT status FROM orders WHERE id = 43"));
        Assert.Contains("Confirmed twice", await client.GetStringAsync("/Admin/Audit"));
    }

    [Fact]
    public async Task Keep_on_the_final_card_leaves_the_order_unchanged_with_no_audit_entry()
    {
        using var app = new AppFactory();
        var client = app.NewClient();
        await client.LoginLevel1Async();
        var (html, _, _) = await SendAsync(client, "cancel 44");
        var id = ActionId.Match(html).Groups[1].Value;
        var token = await client.GetTokenAsync("/Admin/Chat");
        await client.PostFormAsync($"/Admin/Chat?handler=Confirm&id={id}", token);

        var keep = await (await client.PostFormAsync($"/Admin/Chat?handler=Cancel&id={id}", token)).Content.ReadAsStringAsync();

        Assert.Contains("Cancelled", keep);
        Assert.Equal("Pending", await app.QueryAsync<string>("SELECT status FROM orders WHERE id = 44"));
        Assert.Equal(0, await app.QueryAsync<int>("SELECT COUNT(*) FROM audit_entries"));
    }

    [Fact]
    public async Task A_direct_FinalConfirm_without_the_first_confirm_is_refused()
    {
        using var app = new AppFactory();
        var client = app.NewClient();
        await client.LoginLevel1Async();
        var (html, _, _) = await SendAsync(client, "cancel 43");
        var id = ActionId.Match(html).Groups[1].Value;
        var token = await client.GetTokenAsync("/Admin/Chat");

        var res = await client.PostFormAsync($"/Admin/Chat?handler=FinalConfirm&id={id}", token);

        Assert.Equal("Pending", await app.QueryAsync<string>("SELECT status FROM orders WHERE id = 43"));
        Assert.Equal(0, await app.QueryAsync<int>("SELECT COUNT(*) FROM audit_entries"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode); // answered with the card's unchanged state and a notice
    }

    [Fact]
    public async Task Level2_gets_read_answers_but_no_confirm_buttons_for_changes()
    {
        using var app = new AppFactory();
        var client = app.NewClient();
        await client.LoginLevel2Async();

        var (html, _, _) = await SendAsync(client, "what are the pending orders?");
        Assert.Contains("data-table", html);

        // The scripted model asks to propose; the tool refuses for Level2, so no card is created.
        var (changeHtml, _, _) = await SendAsync(client, "order 42 is dispatched");
        Assert.DoesNotContain("data-card-action", changeHtml);
        Assert.Equal(0, await app.QueryAsync<int>("SELECT COUNT(*) FROM proposed_actions"));
        Assert.Equal("Pending", await app.QueryAsync<string>("SELECT status FROM orders WHERE id = 42"));
    }

    [Fact]
    public async Task Chat_posts_without_an_anti_forgery_token_are_refused_and_bad_input_is_400()
    {
        using var app = new AppFactory();
        var client = app.NewClient();
        await client.LoginLevel1Async();

        var noToken = await client.PostAsync("/Admin/Chat?handler=Send", new FormUrlEncodedContent([new KeyValuePair<string, string>("text", "hi")]));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);

        var (_, _, empty) = await SendAsync(client, "   ");
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        var (_, _, tooLong) = await SendAsync(client, new string('x', 1001));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task The_chat_page_needs_sign_in_and_lists_only_my_conversations()
    {
        using var app = new AppFactory();
        var anon = app.NewClient();
        Assert.Equal(HttpStatusCode.Redirect, (await anon.GetAsync("/Admin/Chat")).StatusCode);

        var a = app.NewClient();
        await a.LoginLevel1Async();
        await SendAsync(a, "my private question about pending orders");
        var b = app.NewClient();
        await b.LoginLevel2Async();

        Assert.Contains("my private question", await a.GetStringAsync("/Admin/Chat"));
        Assert.DoesNotContain("my private question", await b.GetStringAsync("/Admin/Chat"));
        Assert.Contains("href=\"/Admin/Chat\"", await a.GetStringAsync("/Admin")); // the nav item
    }
}
