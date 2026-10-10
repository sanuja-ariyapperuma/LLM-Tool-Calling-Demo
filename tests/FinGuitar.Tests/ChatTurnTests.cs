using System.Net;
using FinGuitar.Web.Chat;
using FinGuitar.Web.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinGuitar.Tests;

public class ThinkStripperTests
{
    [Theory]
    [InlineData("<think>secret plan</think>Hello", "Hello")]
    [InlineData("Hello <think>a\nb</think> there", "Hello  there")]
    [InlineData("<think>never closed and long", "")]
    [InlineData("no tags", "no tags")]
    [InlineData("", "")]
    public void Think_blocks_are_removed(string input, string expected) => Assert.Equal(expected, ThinkStripper.Strip(input));

    [Fact]
    public void Null_is_empty() => Assert.Equal("", ThinkStripper.Strip(null));
}

public class ChatTurnTests
{
    private static ChatService NewService(ChatTestEnv c, ScriptedClient inner, int timeoutSeconds = 5)
    {
        IChatClient client = new ChatClientBuilder(inner)
            .UseFunctionInvocation(configure: f => f.MaximumIterationsPerRequest = ChatService.MaxToolRounds)
            .Build();
        return new ChatService(c.Env.Db.Db, c.Store, c.Actions, c.Catalog, c.Log, c.Env.User, client,
            Options.Create(new OllamaOptions { TimeoutSeconds = timeoutSeconds }), NullLogger<ChatService>.Instance);
    }

    private static Task<List<FinGuitar.Web.Chat.ChatMessage>> Messages(ChatTestEnv c, int conversationId) =>
        c.Store.GetMessagesAsync(conversationId).ContinueWith(t => t.Result.ToList());

    [Fact]
    public async Task A_data_turn_saves_both_messages_and_the_last_tool_result_as_the_payload()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var svc = NewService(c, new ScriptedClient((msgs, _) => ScriptedClient.LastIsToolResult(msgs)
            ? ScriptedClient.Say("There are some pending orders.")
            : ScriptedClient.Call("search_orders", ("status", "Pending"))));

        var result = await svc.SendAsync(null, "what are the pending orders?");

        Assert.Equal(2, result.NewMessages.Count);
        var (user, assistant) = (result.NewMessages[0], result.NewMessages[1]);
        Assert.Equal(MessageRole.User, user.Role);
        Assert.Equal("what are the pending orders?", user.Text);
        Assert.Equal(MessageRole.Assistant, assistant.Role);
        Assert.Equal("There are some pending orders.", assistant.Text);
        Assert.False(assistant.IsError);
        Assert.Equal(PayloadKinds.OrderTable, assistant.PayloadKind);
        var table = (OrderTablePayload)ChatPayloadJson.Deserialize(assistant.PayloadKind, assistant.PayloadJson)!;
        Assert.Contains(table.Rows, r => r.Id == 42);
        Assert.All(table.Rows, r => Assert.Equal("Pending", r.Status));

        // saved and reloadable (FR-C11)
        var saved = await Messages(c, result.Conversation.Id);
        Assert.Equal(2, saved.Count);
        Assert.Equal(assistant.PayloadJson, saved[1].PayloadJson);
    }

    [Fact]
    public async Task Search_then_details_shows_the_details()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var step = 0;
        var svc = NewService(c, new ScriptedClient((msgs, _) => ++step switch
        {
            1 => ScriptedClient.Call("search_orders", ("customerName", "John"), ("productName", "Les Paul")),
            2 => ScriptedClient.Call("get_order_details", ("orderId", 30)),
            _ => ScriptedClient.Say("Found it."),
        }));

        var result = await svc.SendAsync(null, "John's Les Paul order last week");
        Assert.Equal(PayloadKinds.OrderDetail, result.NewMessages[1].PayloadKind);
    }

    [Fact]
    public async Task A_turn_without_tool_results_has_no_payload()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var svc = NewService(c, new ScriptedClient((_, _) => ScriptedClient.Say("Hello! Ask me about orders.")));
        var result = await svc.SendAsync(null, "hi");
        Assert.Null(result.NewMessages[1].PayloadKind);
        Assert.Null(result.NewMessages[1].PayloadJson);
    }

    [Fact]
    public async Task A_proposal_turn_shows_the_card_links_the_action_and_changes_nothing()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var svc = NewService(c, new ScriptedClient(ScriptedClient.Sensible));

        var result = await svc.SendAsync(null, "order 42 is dispatched");

        var assistant = result.NewMessages[1];
        Assert.Equal(PayloadKinds.ProposalCard, assistant.PayloadKind);
        var card = (ProposalCardPayload)ChatPayloadJson.Deserialize(assistant.PayloadKind, assistant.PayloadJson)!;
        var action = (await c.Actions.GetForViewAsync(card.ActionId))!;
        Assert.Equal(assistant.Id, action.MessageId);
        Assert.Equal(result.NewMessages[0].Id, action.RequestMessageId);
        Assert.Equal(ProposedActionStatus.Pending, action.Status);
        Assert.Equal("Pending", await c.Scalar<string>("SELECT status FROM orders WHERE id = 42"));
    }

    [Fact]
    public async Task Sending_another_message_expires_the_open_card_in_that_conversation()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var svc = NewService(c, new ScriptedClient(ScriptedClient.Sensible));
        var first = await svc.SendAsync(null, "order 42 is dispatched");
        var card = (ProposalCardPayload)ChatPayloadJson.Deserialize(first.NewMessages[1].PayloadKind, first.NewMessages[1].PayloadJson)!;

        await svc.SendAsync(first.Conversation.Id, "yes");

        Assert.Equal(ProposedActionStatus.Expired, (await c.Actions.GetForViewAsync(card.ActionId))!.Status);
    }

    [Fact]
    public async Task The_model_gets_the_prompt_the_role_tools_and_think_off_and_the_history_has_payload_summaries()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var client = new ScriptedClient((msgs, _) => ScriptedClient.LastIsToolResult(msgs)
            ? ScriptedClient.Say("Here.")
            : msgs.Count(m => m.Role == ChatRole.User) == 1 ? ScriptedClient.Call("search_orders", ("status", "Pending")) : ScriptedClient.Say("Second."));
        var svc = NewService(c, client);

        var first = await svc.SendAsync(null, "pending orders");
        await svc.SendAsync(first.Conversation.Id, "the second one");

        var last = client.Seen[^1];
        Assert.Equal(ChatRole.System, last.Messages[0].Role);
        Assert.Contains("Friday 2026-10-09", last.Messages[0].Text);
        Assert.Contains("Level1", last.Messages[0].Text);
        Assert.Contains("yyyy-MM-dd", last.Messages[0].Text);
        Assert.Contains(last.Messages, m => m.Role == ChatRole.User && m.Text == "pending orders");
        Assert.Contains(last.Messages, m => m.Role == ChatRole.Assistant && m.Text.Contains("[Shown: orders"));
        Assert.Equal(0.1f, last.Options!.Temperature);
        Assert.Equal(false, last.Options.AdditionalProperties!["think"]);
        Assert.Equal(9, last.Options.Tools!.Count);
    }

    [Fact]
    public async Task A_Level2_user_gets_only_the_read_tools()
    {
        await using var c = await ChatTestEnv.CreateAsync(TestEnv.Level2Id);
        var client = new ScriptedClient((_, _) => ScriptedClient.Say("ok"));
        await NewService(c, client).SendAsync(null, "hello");

        var tools = client.Seen[0].Options!.Tools!.Select(t => t.Name).ToList();
        Assert.Equal(7, tools.Count);
        Assert.DoesNotContain(tools, n => n.StartsWith("propose_"));
        Assert.Contains("Level2", client.Seen[0].Messages[0].Text);
    }

    [Fact]
    public async Task History_is_limited_to_20_messages_and_leaves_out_error_messages()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var calls = 0;
        var client = new ScriptedClient((_, _) => ++calls == 1 ? throw new HttpRequestException("down") : ScriptedClient.Say("fine"));
        var svc = NewService(c, client);

        var first = await svc.SendAsync(null, "first question");
        Assert.True(first.NewMessages[1].IsError);
        await svc.SendAsync(first.Conversation.Id, "second question");

        var seen = client.Seen[^1].Messages;
        Assert.DoesNotContain(seen, m => m.Text.Contains("not available"));

        for (var i = 0; i < 15; i++) await svc.SendAsync(first.Conversation.Id, $"filler {i}");
        // system prompt + at most 20 history messages
        Assert.True(client.Seen[^1].Messages.Count <= 21);
    }

    [Fact]
    public async Task Ollama_unreachable_gives_a_saved_error_message_and_the_app_keeps_working()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var svc = NewService(c, new ScriptedClient((_, _) => throw new HttpRequestException("connection refused")));

        var result = await svc.SendAsync(null, "hello");

        var reply = result.NewMessages[1];
        Assert.True(reply.IsError);
        Assert.Equal("The assistant is not available right now. Check that Ollama is running.", reply.Text);
        Assert.Equal(2, (await Messages(c, result.Conversation.Id)).Count);
    }

    [Fact]
    public async Task A_missing_model_says_it_is_still_downloading()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var svc = NewService(c, new ScriptedClient((_, _) => throw new HttpRequestException("not found", null, HttpStatusCode.NotFound)));

        var reply = (await svc.SendAsync(null, "hello")).NewMessages[1];

        Assert.True(reply.IsError);
        Assert.Equal("The assistant is still downloading its model. Try again in a few minutes.", reply.Text);
    }

    [Fact]
    public async Task A_timeout_gives_the_unavailable_message()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var svc = NewService(c, new ScriptedClient((_, _) => throw new OperationCanceledException()));
        var reply = (await svc.SendAsync(null, "hello")).NewMessages[1];
        Assert.True(reply.IsError);
        Assert.Contains("not available", reply.Text);
    }

    [Fact]
    public async Task Any_other_failure_gives_a_generic_message_and_does_not_crash()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var svc = NewService(c, new ScriptedClient((_, _) => throw new InvalidOperationException("boom")));
        var reply = (await svc.SendAsync(null, "hello")).NewMessages[1];
        Assert.True(reply.IsError);
        Assert.Equal("Something went wrong. Please try again.", reply.Text);
    }

    [Fact]
    public async Task Reaching_the_round_limit_gives_the_D22_message_with_IsError_and_keeps_the_last_payload()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var svc = NewService(c, new ScriptedClient((_, _) => ScriptedClient.Call("search_orders", ("status", "Pending"))));

        var reply = (await svc.SendAsync(null, "loop forever")).NewMessages[1];

        Assert.True(reply.IsError);
        Assert.Equal("I couldn't finish that. Try asking more specifically.", reply.Text);
        Assert.Equal(PayloadKinds.OrderTable, reply.PayloadKind);
    }

    [Fact]
    public async Task A_copied_Shown_note_is_removed_from_the_reply()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var svc = NewService(c, new ScriptedClient((_, _) => ScriptedClient.Say("Here you go. [Shown: orders 1, 2] Thanks.")));
        var reply = (await svc.SendAsync(null, "hello")).NewMessages[1];
        Assert.Equal("Here you go.  Thanks.", reply.Text);
    }

    [Fact]
    public async Task Saying_please_confirm_without_a_card_is_flagged_but_a_real_card_is_not()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var svc = NewService(c, new ScriptedClient((_, _) => ScriptedClient.Say("Order 43 is being cancelled. Please confirm this change.")));
        var reply = (await svc.SendAsync(null, "cancel order 43")).NewMessages[1];
        Assert.EndsWith(ChatService.NoCardNote, reply.Text);

        await using var d = await ChatTestEnv.CreateAsync();
        var real = NewService(d, new ScriptedClient(ScriptedClient.Sensible));
        Assert.DoesNotContain(ChatService.NoCardNote, (await real.SendAsync(null, "order 42 is dispatched")).NewMessages[1].Text);
    }

    [Fact]
    public async Task Think_text_is_stripped_before_saving()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var svc = NewService(c, new ScriptedClient((_, _) => ScriptedClient.Say("<think>I should be careful</think>All good.")));
        var reply = (await svc.SendAsync(null, "hello")).NewMessages[1];
        Assert.Equal("All good.", reply.Text);
        Assert.DoesNotContain("think", reply.Text);
    }

    [Fact]
    public async Task Empty_and_over_long_messages_are_rejected_and_1000_characters_are_allowed()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var svc = NewService(c, new ScriptedClient((_, _) => ScriptedClient.Say("ok")));

        await Assert.ThrowsAsync<ValidationException>(() => svc.SendAsync(null, ""));
        await Assert.ThrowsAsync<ValidationException>(() => svc.SendAsync(null, "   "));
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.SendAsync(null, new string('x', 1001)));
        Assert.Contains("1,000", ex.Message);
        Assert.Equal(0, await c.Scalar<int>("SELECT COUNT(*) FROM chat_messages WHERE text = 'x'"));

        await svc.SendAsync(null, new string('x', 1000));
    }

    [Fact]
    public async Task The_title_is_the_first_message_cut_to_60_characters()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var svc = NewService(c, new ScriptedClient((_, _) => ScriptedClient.Say("ok")));

        var result = await svc.SendAsync(null, new string('a', 80));
        Assert.Equal(new string('a', 60), result.Conversation.Title);

        var again = await svc.SendAsync(result.Conversation.Id, "something else");
        Assert.Equal(new string('a', 60), again.Conversation.Title); // the title does not change
    }

    [Fact]
    public async Task A_conversation_that_belongs_to_someone_else_is_not_found()
    {
        await using var c = await ChatTestEnv.CreateAsync();
        var svc = NewService(c, new ScriptedClient((_, _) => ScriptedClient.Say("ok")));
        var mine = await svc.SendAsync(null, "hello");

        var other = ChatTestEnv.Another(c, TestEnv.Level2Id);
        var otherSvc = NewService(other, new ScriptedClient((_, _) => ScriptedClient.Say("ok")));

        await Assert.ThrowsAsync<NotFoundException>(() => otherSvc.SendAsync(mine.Conversation.Id, "hi"));
        Assert.Null(await other.Store.GetConversationAsync(mine.Conversation.Id, TestEnv.Level2Id));
        Assert.Empty(await other.Store.ListConversationsAsync(TestEnv.Level2Id));
        Assert.Equal(2, (await c.Store.ListConversationsAsync(TestEnv.Level1Id)).Count); // the test setup's own plus this one
    }
}
