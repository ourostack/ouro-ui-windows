using System.Text.Json.Nodes;
using Ouro.Actions;
using OuroShop;
using OuroShop.Actions;
using Xunit;

namespace Ouro.Actions.Tests;

public class RuntimeTests
{
    private static OuroActionRuntime Runtime(OuroMemoryAudit? audit = null) => new(OuroActions.Catalog, ShoppingHandler.Handle, audit);

    private static OuroCall Call(string id, string json, OuroDoor door) => new(id, JsonNode.Parse(json)!.AsObject(), door);

    [Fact]
    public async Task A_person_only_action_is_refused_at_every_agent_door_with_cores_refusal_byte_for_byte()
    {
        var runtime = Runtime();
        foreach (var door in new[] { OuroDoor.Mcp, OuroDoor.AppAction })
        {
            var outcome = await runtime.InvokeAsync(Call("place_grocery_order", "{}", door));
            Assert.Equal(OuroOutcomeState.Refused, outcome.State);
            Assert.Equal(OuroActions.PlaceGroceryOrder.RefusalJson, OuroJson.Write(outcome.Body));
            Assert.Equal("person-only", outcome.Code);
        }
        Assert.Equal(OuroOutcomeState.Done, (await runtime.InvokeAsync(Call("place_grocery_order", "{}", OuroDoor.Person))).State);
    }

    [Fact]
    public async Task Input_is_checked_against_the_schema_and_every_problem_is_named()
    {
        var outcome = await Runtime().InvokeAsync(Call("add_shopping_list_item", """{"quantity":-1,"colour":"red"}""", OuroDoor.Mcp));
        Assert.Equal(OuroOutcomeState.Invalid, outcome.State);
        Assert.Equal("invalid-input", outcome.Code);
        var problems = outcome.Body["error"]!["problems"]!.AsArray().Select(p => p!.GetValue<string>()).ToList();
        Assert.Contains(problems, p => p.StartsWith("input.name: is required"));
        Assert.Contains(problems, p => p.StartsWith("input.quantity: is -1, below its minimum 0"));
        Assert.Contains(problems, p => p.StartsWith("input.colour: is not an input this action takes"));
        var email = await Runtime().InvokeAsync(Call("share_shopping_list", """{"with":"not an email"}""", OuroDoor.Person));
        Assert.Contains("is not a valid email", email.Summary);
    }

    [Fact]
    public async Task A_retry_with_the_same_key_answers_the_first_result_without_running_again()
    {
        var runtime = Runtime();
        var first = await runtime.InvokeAsync(Call("add_shopping_list_item", """{"name":"oat milk","idempotencyKey":"k1"}""", OuroDoor.Mcp));
        var again = await runtime.InvokeAsync(Call("add_shopping_list_item", """{"name":"oat milk","idempotencyKey":"k1"}""", OuroDoor.Mcp));
        Assert.Equal(first.Body["id"]!.GetValue<string>(), again.Body["id"]!.GetValue<string>());
        Assert.True(again.Replayed);
        var other = await runtime.InvokeAsync(Call("add_shopping_list_item", """{"name":"eggs","idempotencyKey":"k1"}""", OuroDoor.Mcp));
        Assert.Equal("idempotency-conflict", other.Code);
        var later = runtime.Now;
        runtime.Now = () => later().AddHours(25);
        var expired = await runtime.InvokeAsync(Call("add_shopping_list_item", """{"name":"oat milk","idempotencyKey":"k1"}""", OuroDoor.Mcp));
        Assert.False(expired.Replayed);
        Assert.NotEqual(first.Body["id"]!.GetValue<string>(), expired.Body["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_consequential_action_from_an_agent_runs_only_when_the_person_says_yes()
    {
        var runtime = Runtime();
        var share = () => runtime.InvokeAsync(Call("share_shopping_list", """{"with":"sam@example.com"}""", OuroDoor.Mcp));
        Assert.Equal("needs-confirmation", (await share()).Code);
        OuroCall? asked = null;
        runtime.Confirm = (_, _, call, _) => { asked = call; return Task.FromResult(false); };
        Assert.Equal(OuroOutcomeState.Declined, (await share()).State);
        Assert.Equal(OuroDoor.Mcp, asked!.Door);
        runtime.Confirm = (_, _, _, _) => Task.FromResult(true);
        Assert.Equal(OuroOutcomeState.Done, (await share()).State);
        runtime.Confirm = null;
        Assert.Equal(OuroOutcomeState.Done, (await runtime.InvokeAsync(Call("share_shopping_list", """{"with":"sam@example.com"}""", OuroDoor.Person))).State);
    }

    [Fact]
    public async Task The_person_check_is_off_by_default_and_refuses_when_on_and_not_verified()
    {
        var runtime = Runtime();
        Assert.Null(runtime.PersonCheck);
        runtime.PersonCheck = (_, _) => Task.FromResult(new OuroPersonCheckResult(false, "Windows Hello isn't set up on this PC."));
        var outcome = await runtime.InvokeAsync(Call("place_grocery_order", "{}", OuroDoor.Person));
        Assert.Equal("person-not-verified", outcome.Code);
        Assert.Contains("Windows Hello isn't set up", outcome.Summary);
        Assert.Equal(OuroOutcomeState.Done, (await runtime.InvokeAsync(Call("add_shopping_list_item", """{"name":"eggs"}""", OuroDoor.Person))).State);
    }

    [Fact]
    public async Task Every_call_is_audited_with_its_door_caller_and_outcome_and_no_input_values()
    {
        var audit = new OuroMemoryAudit();
        var runtime = Runtime(audit);
        await runtime.InvokeAsync(Call("place_grocery_order", "{}", OuroDoor.Mcp) with { Caller = "probe" });
        await runtime.InvokeAsync(Call("add_shopping_list_item", """{"name":"oat milk"}""", OuroDoor.AppAction));
        await runtime.InvokeAsync(Call("no_such_action", "{}", OuroDoor.Mcp));
        Assert.Equal(new[] { OuroOutcomeState.Refused, OuroOutcomeState.Done, OuroOutcomeState.Invalid }, audit.Entries.Select(e => e.State));
        Assert.Equal("probe", audit.Entries[0].Caller);
        Assert.Equal(new[] { "name" }, audit.Entries[1].InputNames);
        Assert.DoesNotContain("oat milk", string.Join(" ", audit.Entries.Select(e => e.ToString())));
    }

    [Fact]
    public async Task A_cancelled_call_says_so_and_a_throwing_handler_fails_without_throwing()
    {
        var runtime = new OuroActionRuntime(OuroActions.Catalog, async (_, _, ct) => { await Task.Delay(1000, ct); return new JsonObject(); });
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Equal(OuroOutcomeState.Cancelled, (await runtime.InvokeAsync(Call("add_shopping_list_item", """{"name":"x"}""", OuroDoor.Person), cts.Token)).State);
        var throwing = new OuroActionRuntime(OuroActions.Catalog, (_, _, _) => throw new InvalidOperationException("the store is closed"));
        var failed = await throwing.InvokeAsync(Call("add_shopping_list_item", """{"name":"x"}""", OuroDoor.Person));
        Assert.Equal(OuroOutcomeState.Failed, failed.State);
        Assert.Contains("the store is closed", failed.Summary);
    }
}

public class DoorTests
{
    private static OuroActionRuntime Runtime() => new(OuroActions.Catalog, ShoppingHandler.Handle);

    [Fact]
    public async Task An_App_Action_URI_is_decoded_once_and_its_token_keys_idempotency()
    {
        var call = OuroAppActionUri.Read(OuroActions.Catalog, new Uri("ouro-shop://action/add_shopping_list_item?name=oat%20milk%20%26%20honey&token=T1"))!;
        Assert.Equal("oat milk & honey", call.Input["name"]!.GetValue<string>());
        Assert.Equal("app-action:T1", call.IdempotencyKey);
        var runtime = Runtime();
        var first = await runtime.InvokeAsync(call);
        Assert.True((await runtime.InvokeAsync(call)).Replayed);
        Assert.Equal(OuroOutcomeState.Done, first.State);
        Assert.Null(OuroAppActionUri.Read(OuroActions.Catalog, new Uri("other://action/add_shopping_list_item")));
        var spoofed = OuroAppActionUri.Read(OuroActions.Catalog, new Uri("ouro-shop://action/place_grocery_order?token=spoofed"))!;
        Assert.Equal("person-only", (await runtime.InvokeAsync(spoofed)).Code);
    }

    [Fact]
    public async Task The_MCP_server_lists_cores_tools_and_answers_a_person_only_call_with_the_refusal()
    {
        var server = new OuroMcpServer(Runtime());
        var init = await server.HandleAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","clientInfo":{"name":"probe"}}}""");
        Assert.Equal("OuroShop.Mcp", init!["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        var list = await server.HandleAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        var names = list!["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToArray();
        Assert.Equal(new[] { "add_shopping_list_item", "share_shopping_list" }, names);
        var refused = await server.HandleAsync("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"place_grocery_order","arguments":{}}}""");
        Assert.True(refused!["result"]!["isError"]!.GetValue<bool>());
        Assert.Equal(OuroActions.PlaceGroceryOrder.RefusalJson, OuroJson.Write(refused["result"]!["structuredContent"]!));
        Assert.Null(await server.HandleAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
        Assert.Equal(-32602, (await server.HandleAsync("""{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"nope"}}"""))!["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task The_MCP_server_serves_over_streams()
    {
        var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"add_shopping_list_item","arguments":{"name":"eggs"},"_meta":{"ouro/idempotencyKey":"a"}}}""" + "\n"));
        var output = new MemoryStream();
        await new OuroMcpServer(Runtime()).RunAsync(input, output);
        var reply = JsonNode.Parse(System.Text.Encoding.UTF8.GetString(output.ToArray()))!;
        Assert.False(reply["result"]!["isError"]!.GetValue<bool>());
        Assert.StartsWith("item_", reply["result"]!["structuredContent"]!["id"]!.GetValue<string>());
    }
}
