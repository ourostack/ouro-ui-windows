using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Ouro.Actions;
using Xunit;

namespace Ouro.Tests;

/// <summary>
/// The C# port of core's A2UI adapter (src/Ouro.Actions/A2UI.cs) against core's A2UI conformance cases, vendored from the
/// core tag (core-provenance.json): each stream resolves to the case's <c>expect</c>, written as core writes it (every
/// surface's tree, or every problem), and every tree it gives passes the composed tree's own validator. Core's
/// test/a2ui.test.mts and test/composed.test.mts are the model for the rest.
/// </summary>
public class A2UITests
{
    private static string Root([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    private static JsonNode Read(string path) => JsonNode.Parse(File.ReadAllText(Path.Combine(Root(), path)))!;

    private static readonly OuroComposedCatalog Hx = OuroComposedCatalog.Load(Read("vendor/core/a2ui/catalog.json"));
    private static readonly OuroComposedCatalog App = OuroComposedCatalog.Load(Read("vendor/core/a2ui/conformance/recipe.catalog.json"));

    /// <summary>The cases core-provenance.json records, so every one is core's at the tag, byte for byte.</summary>
    private static string[] CaseNames() => Read("core-provenance.json")["files"]!.AsObject().Select(f => f.Key)
        .Where(f => f.StartsWith("vendor/core/a2ui/conformance/cases/", StringComparison.Ordinal)).Order(StringComparer.Ordinal)
        .Select(f => Path.GetFileName(f)).ToArray();

    public static TheoryData<string> Cases() => new(CaseNames());

    private static JsonObject Case(string name) => Read($"vendor/core/a2ui/conformance/cases/{name}").AsObject();

    private static OuroComposedCatalog CatalogOf(JsonObject c) => c["catalog"]!.GetValue<string>() == "catalog.json" ? Hx : App;

    private static JsonArray Create(string components, string data = "{}") =>
        JsonNode.Parse($$"""[{ "version": "1.0", "createSurface": { "surfaceId": "s", "catalogId": "{{App.CatalogId}}", "dataModel": {{data}}, "components": {{components}} } }]""")!.AsArray();

    private static IReadOnlyList<OuroA2UIProblem> Problems(string components, string data = "{}") => OuroA2UI.ValidateMessages(App, Create(components, data));

    private static JsonObject Surface(string components, string data = "{}", Func<OuroLabelContext, string?>? label = null)
    {
        var resolved = OuroA2UI.ResolveMessages(App, Create(components, data), label);
        Assert.True(resolved.Valid, string.Join("; ", resolved.Problems));
        return resolved.Surfaces.Single().Tree!;
    }

    [Fact]
    public void Every_case_core_ships_is_here()
    {
        Assert.Equal(13, CaseNames().Length);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Each_case_resolves_to_its_expect(string name)
    {
        var c = Case(name);
        var got = OuroA2UI.ResolveMessages(CatalogOf(c), c["messages"]).ToJson();
        Assert.Equal(OuroJson.Write(c["expect"]!), OuroJson.Write(got));
        Assert.Equal(name.StartsWith("invalid-", StringComparison.Ordinal), got.ContainsKey("problems"));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Each_tree_a_case_gives_passes_the_tree_validator(string name)
    {
        var c = Case(name);
        var resolved = OuroA2UI.ResolveMessages(CatalogOf(c), c["messages"]);
        foreach (var s in resolved.Surfaces.Where(s => s.Tree is not null))
            Assert.True(OuroComposed.ValidateTree(CatalogOf(c), s.Tree).Count == 0, $"{name} {s.SurfaceId}: {string.Join("; ", OuroComposed.ValidateTree(CatalogOf(c), s.Tree))}");
    }

    [Fact]
    public void A_name_counts_only_when_the_catalog_itself_has_it()
    {
        foreach (var name in new[] { "constructor", "toString", "__proto__" })
        {
            Assert.Contains(Problems($$"""[{ "id": "root", "component": "{{name}}" }]"""), p => p.Message.Contains("is not in this catalog"));
            Assert.Contains(Problems($$"""[{ "id": "root", "component": "ActionButton", "action": "{{name}}" }]"""), p => p.Message.Contains("not one of the app's actions"));
            Assert.Contains(Problems($$"""[{ "id": "root", "component": "ActionButton", "action": "share_recipe", "input": { "recipe": "r", "with": "s@e.com", "{{name}}": true } }]"""), p => p.Message.Contains($"share_recipe has no input {name}"));
        }
    }

    [Fact]
    public void No_pointer_reaches_a_prototype()
    {
        var stream = Create("""[{ "id": "root", "component": "Text", "text": "hi" }]""");
        stream.Add(JsonNode.Parse("""{ "version": "1.0", "updateDataModel": { "surfaceId": "s", "path": "/__proto__/polluted", "value": "yes" } }"""));
        Assert.Contains(OuroA2UI.ValidateMessages(App, stream), p => p.Path.EndsWith("/updateDataModel/path", StringComparison.Ordinal));
        foreach (var pointer in new[] { "/__proto__/x", "constructor", "items/__proto__", "/a/prototype" })
        {
            var bound = Problems($$"""[{ "id": "root", "component": "Card", "children": ["f", "b"] }, { "id": "f", "component": "Text", "text": { "@path": "{{pointer}}" } }, { "id": "b", "component": "ActionButton", "action": "share_recipe", "input": { "recipe": { "@path": "{{pointer}}" }, "with": "s@e.com" } }]""");
            Assert.True(bound.Count(p => p.Message.Contains("never through __proto__")) >= 2, pointer);
        }
        Assert.Contains(Problems("""[{ "id": "root", "component": "Stack", "children": { "componentId": "t", "path": "/__proto__" } }, { "id": "t", "component": "Text", "text": "x" }]"""), p => p.Message.Contains("never through __proto__"));
        Assert.Contains(Problems("""[{ "id": "root", "component": "TextField", "label": "L", "value": { "@path": "/constructor" } }]"""), p => p.Message.Contains("never through __proto__"));
    }

    [Fact]
    public void A_button_is_the_catalogs_and_its_label_and_consent_cant_be_sent()
    {
        var forged = Problems("""[{ "id": "root", "component": "ActionButton", "action": "share_recipe", "label": "Free gift", "press": "run", "consent": "read", "input": { "recipe": "r", "with": "s@e.com" } }]""");
        Assert.Equal(["/0/createSurface/components/0/label", "/0/createSurface/components/0/press", "/0/createSurface/components/0/consent"], forged.Select(p => p.Path));
        Assert.All(forged, p => Assert.StartsWith("ActionButton has no ", p.Message));
        var share = Surface("""[{ "id": "root", "component": "ActionButton", "action": "share_recipe", "input": { "recipe": "r", "with": "s@e.com" }, "@accessibility": { "label": "Free gift" } }]""");
        Assert.Equal("Share recipe", share["props"]!["label"]!.GetValue<string>());
        Assert.Equal("confirm", share["props"]!["press"]!.GetValue<string>());
        Assert.Equal("@label", share["props"]!.AsObject().Last().Key);
        Assert.Contains(Problems("""[{ "id": "root", "component": "Button", "label": "Pay" }]"""), p => p.Message.StartsWith("\"Button\" is not in this catalog", StringComparison.Ordinal));
        var order = Problems("""[{ "id": "root", "component": "ActionButton", "action": "order_ingredients", "input": { "recipe": "r1" } }]""");
        Assert.Equal("person-only", order.Single().Refusal!["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public void A_button_bound_to_data_no_field_writes_reads_it_now_and_never_follows_a_value_shaped_like_a_binding()
    {
        const string button = """[{ "id": "root", "component": "Card", "children": ["share"] }, { "id": "share", "component": "ActionButton", "action": "share_recipe", "input": { "recipe": { "@path": "/recipe" }, "with": "sam@example.com" }, "@accessibility": { "label": "Share it" } }]""";
        var object_ = Surface(button, """{ "recipe": { "id": "r1" } }""")["children"]![0]!;
        Assert.Equal("""{"recipe":{"id":"r1"},"with":"sam@example.com"}""", OuroJson.Write(object_["props"]!["input"]!));
        var posing = Surface(button, """{ "recipe": { "@path": "/secret" }, "secret": "S" }""")["children"]![0]!;
        Assert.Equal("""{"@path":"/secret"}""", posing["props"]!["input"]!["recipe"]!.GetValue<string>());
        var press = OuroComposed.PersonPress(OuroComposedNode.FromJson(posing), true, JsonNode.Parse("""{ "secret": "S" }"""));
        Assert.Equal("""{"@path":"/secret"}""", press.Input["recipe"]!.GetValue<string>());
        Assert.Equal("@label", posing["props"]!.AsObject().Last().Key);
    }

    [Fact]
    public void A_button_bound_to_a_field_stays_bound_and_sends_what_the_person_entered()
    {
        var tree = Surface("""[{ "id": "root", "component": "Card", "children": ["servings", "shop"] }, { "id": "servings", "component": "TextField", "label": "Servings", "value": { "@path": "/servings" }, "type": "number" }, { "id": "shop", "component": "ActionButton", "action": "add_recipe_to_shopping_list", "input": { "recipe": { "@path": "/recipe" }, "servings": { "@path": "servings" } } }]""", """{ "recipe": "rcp_1", "servings": 2 }""");
        var shop = OuroComposedNode.FromJson(tree["children"]![1]);
        Assert.Equal("""{"recipe":"rcp_1","servings":{"@path":"/servings"}}""", OuroJson.Write(shop.Props["input"]!));
        Assert.Equal("""{"recipe":"rcp_1","servings":6}""", OuroJson.Write(OuroComposed.PersonPress(shop, false, JsonNode.Parse("""{ "recipe": "rcp_1", "servings": 6 }""")).Input));
    }

    [Fact]
    public void The_apps_resolver_names_an_id_and_only_an_id()
    {
        var seen = new List<string>();
        var tree = Surface("""[{ "id": "root", "component": "ActionButton", "action": "share_recipe", "input": { "recipe": { "@path": "/recipe/id" }, "recipeTitle": "Something else", "with": "sam@example.com" } }]""",
            """{ "recipe": { "id": "rcp_lemon_orzo", "title": "Lemon orzo" } }""",
            c => { seen.Add($"{c.Action} {c.Field} {c.Value}"); return c.Field == "recipe" ? "Lemon orzo (yours)" : null; });
        Assert.Equal("""[{"label":"Recipe","value":"Lemon orzo (yours)","raw":"rcp_lemon_orzo"},{"label":"Send to","value":"sam@example.com"}]""", OuroJson.Write(tree["props"]!["confirm"]!["fields"]!));
        Assert.Equal(["share_recipe recipe rcp_lemon_orzo"], seen);
    }

    [Fact]
    public void The_envelope_the_surfaces_and_the_tree()
    {
        Assert.Empty(Problems("""[{ "id": "root", "component": "Text", "text": "Hi" }]"""));
        Assert.Contains("\"version\": \"1.0\"", OuroA2UI.ValidateMessages(App, JsonNode.Parse("""[{ "version": "0.9", "createSurface": {} }]"""))[0].Message);
        Assert.Contains("Create surface x", OuroA2UI.ValidateMessages(App, JsonNode.Parse("""[{ "version": "1.0", "updateComponents": { "surfaceId": "x", "components": [] } }]"""))[0].Message);
        var twice = Create("""[{ "id": "root", "component": "Text", "text": "Hi" }]""");
        twice.Add(twice[0]!.DeepClone());
        Assert.Equal("INTEGRITY_ERROR", OuroA2UI.ValidateMessages(App, twice)[0].Code);
        Assert.Contains("id root", Problems("""[{ "id": "a", "component": "Text", "text": "Hi" }]""")[0].Message);
        Assert.Contains(Problems("""[{ "id": "root", "component": "Card", "children": ["a"] }, { "id": "a", "component": "Card", "children": ["root"] }]"""), p => p.Message == "root can't be a child.");
        Assert.Contains(Problems("""[{ "id": "root", "component": "Card", "children": ["a"] }, { "id": "a", "component": "Card", "children": ["b"] }, { "id": "b", "component": "Card", "children": ["a"] }]"""), p => p.Message == "The tree loops: root > a > b > a.");
        Assert.Contains("needs description", Problems("""[{ "id": "root", "component": "Image", "url": "https://x/y.jpg" }]""")[0].Message);
        Assert.Contains("is not an identifier", Problems("""[{ "id": "1root", "component": "Text", "text": "Hi" }]""")[0].Message);
        Assert.Empty(Problems("""[{ "id": "root", "component": "Card", "children": ["été_1"] }, { "id": "été_1", "component": "Text", "text": "Hi" }]"""));
        Assert.Equal("Send a list of A2UI messages.", OuroA2UI.ValidateMessages(App, JsonNode.Parse("{}")).Single().Message);
    }

    [Fact]
    public void A_stream_that_names_a_key_twice_is_a_problem_not_an_exception()
    {
        var problems = OuroA2UI.ValidateMessages(App, JsonNode.Parse("""[{ "version": "1.0", "version": "1.0" }]"""));
        Assert.Contains("names a key twice", problems.Single().Message);
        Assert.False(OuroA2UI.ResolveMessages(App, JsonNode.Parse("""[{ "version": "1.0", "version": "1.0" }]""")).Valid);
    }

    [Fact]
    public void An_agent_cant_press_a_button_through_the_screen()
    {
        Assert.Contains("through their own tool (MCP share_recipe)", OuroA2UI.FunctionCallProblem(App, JsonValue.Create("share_recipe")).Message);
        var person = OuroA2UI.FunctionCallProblem(App, JsonValue.Create("order_ingredients"));
        Assert.Equal("refused", person.Refusal!["state"]!.GetValue<string>());
        Assert.Contains("no functions an agent may call, so openUrl is not one", OuroA2UI.FunctionCallProblem(App, JsonValue.Create("openUrl")).Message);
    }

    [Fact]
    public void A_press_is_the_A2UI_action_message_that_says_the_person_pressed_it()
    {
        var c = Case("action-buttons.json");
        var tree = OuroComposedNode.FromJson(OuroA2UI.ResolveMessages(App, c["messages"]).Surfaces.Single().Tree);
        var (open, shop, share, order) = (tree.Children[0], tree.Children[1], tree.Children[2], tree.Children[3]);
        Assert.Equal(
            """{"version":"1.0","action":{"name":"add_recipe_to_shopping_list","surfaceId":"buttons","sourceComponentId":"shop","timestamp":"2026-11-13T12:00:00Z","userMessage":"Add ingredients to list","context":{"recipe":"rcp_123","servings":4},"metadata":{"extensions":{"bot_ouroboros_action":{"consent":"write","by":"person"}}}}}""",
            OuroJson.Write(OuroA2UI.ActionMessage("buttons", shop, "2026-11-13T12:00:00Z")));
        Assert.Equal("get_recipe", OuroA2UI.ActionMessage("b", open, "t")["action"]!["name"]!.GetValue<string>());
        Assert.Throws<InvalidOperationException>(() => OuroA2UI.ActionMessage("b", share, "t"));
        Assert.Equal("""{"extensions":{"bot_ouroboros_action":{"consent":"consequential","by":"person","confirmed":true}}}""", OuroJson.Write(OuroA2UI.ActionMessage("b", share, "t", confirmed: true)["action"]!["metadata"]!));
        Assert.Throws<InvalidOperationException>(() => OuroA2UI.ActionMessage("b", order, "t", confirmed: true));
        Assert.Equal("2026-11-13T12:00:00.000Z", OuroA2UI.Timestamp(new DateTimeOffset(2026, 11, 13, 4, 0, 0, TimeSpan.FromHours(-8))));
    }

    [Fact]
    public void Data_model_writes_follow_JSON_pointer_and_an_empty_relative_path_is_the_template_item()
    {
        var resolved = OuroA2UI.ResolveMessages(Hx, JsonNode.Parse($$"""
            [{ "version": "1.0", "createSurface": { "surfaceId": "s", "catalogId": "{{Hx.CatalogId}}", "dataModel": { "a": { "b": 1 }, "list": [1, 2, 3] } } },
             { "version": "1.0", "updateDataModel": { "surfaceId": "s", "path": "/a/c~1d", "value": "x" } },
             { "version": "1.0", "updateDataModel": { "surfaceId": "s", "path": "/list/1", "value": null } },
             { "version": "1.0", "updateDataModel": { "surfaceId": "s", "path": "/new/deep", "value": true } }]
            """));
        Assert.Equal("""{"a":{"b":1,"c/d":"x"},"list":[1,3],"new":{"deep":true}}""", OuroJson.Write(resolved.Surfaces.Single().Data!));
        Assert.Null(resolved.Surfaces.Single().Tree);
        var tags = OuroA2UI.ResolveMessages(Hx, JsonNode.Parse($$"""
            [{ "version": "1.0", "createSurface": { "surfaceId": "s", "catalogId": "{{Hx.CatalogId}}", "dataModel": { "tags": ["Vegetarian", "One pan"] }, "components": [
              { "id": "root", "component": "Stack", "children": { "componentId": "tag", "path": "/tags" } },
              { "id": "tag", "component": "Badge", "text": { "@path": "" } }] } }]
            """)).Surfaces.Single().Tree!;
        Assert.Equal(["Vegetarian", "One pan"], tags["children"]!.AsArray().Select(c => c!["props"]!["text"]!.GetValue<string>()));
    }

    [Fact]
    public void A_stream_too_deep_or_too_wide_is_refused_not_a_crash()
    {
        JsonArray Stream(int count, Func<int, JsonNode[]> children)
        {
            var components = new JsonArray();
            for (var i = 0; i < count; i++) components.Add(new JsonObject { ["id"] = i == 0 ? "root" : $"c{i}", ["component"] = "Card", ["children"] = new JsonArray(children(i)) });
            var stream = Create("[]");
            stream[0]!["createSurface"]!["components"] = components;
            return stream;
        }
        var deep = Stream(20000, i => i == 19999 ? [] : [JsonValue.Create($"c{i + 1}")]);
        Assert.Contains(OuroA2UI.ValidateMessages(App, deep), p => p.Message == "The tree is more than 24 components deep at c24; nest less.");
        Assert.False(OuroA2UI.ResolveMessages(App, deep).Valid);
        var wide = Stream(20, i => i == 19 ? [] : [JsonValue.Create($"c{i + 1}"), JsonValue.Create($"c{i + 1}")]);
        Assert.Empty(OuroA2UI.ValidateMessages(App, wide));
        Assert.Equal("Surface s resolves to more than 5000 components; show fewer, or fewer list items.", OuroA2UI.ResolveMessages(App, wide).Problems.Single().Message);
        var big = Create("""[{ "id": "root", "component": "Stack", "children": { "componentId": "row", "path": "/rows" } }, { "id": "row", "component": "List", "items": { "@path": "/big" } }]""");
        big[0]!["createSurface"]!["dataModel"] = new JsonObject
        {
            ["rows"] = new JsonArray(Enumerable.Range(0, 2000).Select(i => (JsonNode)i).ToArray()),
            ["big"] = new JsonArray(Enumerable.Range(0, 1000).Select(_ => (JsonNode)"item").ToArray()),
        };
        Assert.Equal("Surface s resolves to more than 1000000 characters of data; show less of it, or fewer list items.", OuroA2UI.ResolveMessages(App, big).Problems.Single().Message);
    }

    [Fact]
    public void Keys_and_numbers_read_as_JavaScript_holds_them()
    {
        var resolved = OuroA2UI.ResolveMessages(App, JsonNode.Parse($$"""
            [{ "version": "1.0", "createSurface": { "surfaceId": "b", "catalogId": "{{App.CatalogId}}", "components": [{ "id": "root", "component": "Heading", "text": "B", "level": 2.0 }] } },
             { "version": "1.0", "createSurface": { "surfaceId": "1", "catalogId": "{{App.CatalogId}}", "dataModel": { "o": { "b": 1.50, "1": 2 } }, "components": [{ "id": "root", "component": "Text", "text": { "@path": "/o" } }] } }]
            """));
        Assert.Equal(["1", "b"], resolved.Surfaces.Select(s => s.SurfaceId));
        Assert.Equal("""{"surfaces":{"1":{"id":"root","component":"Text","props":{"text":"{\"1\":2,\"b\":1.5}","tone":"default"},"children":[]},"b":{"id":"root","component":"Heading","props":{"text":"B","level":2},"children":[]}}}""", OuroJson.Write(resolved.ToJson()));
        Assert.Contains("is not an identifier", Problems("""[{ "id": "\u309b", "component": "Text", "text": "Hi" }]""")[0].Message);
    }

    [Fact]
    public void Ouro_Shops_A2UI_card_is_its_composed_card()
    {
        var shop = OuroComposedCatalog.Load(Read("samples/OuroShop/Composed/catalog.json"));
        var card = Read("samples/OuroShop/Composed/card.json")["structuredContent"]!["ouro"]!;
        Assert.True(OuroA2UI.TryReadToolResult(Read("samples/OuroShop/Composed/card.a2ui.json"), out var messages));
        var surface = OuroA2UI.ResolveMessages(shop, messages).Surfaces.Single();
        Assert.True(JsonNode.DeepEquals(card["tree"], surface.Tree));
        Assert.True(JsonNode.DeepEquals(card["data"], surface.Data));
        Assert.False(OuroA2UI.TryReadToolResult(Read("samples/OuroShop/Composed/card.json"), out _));
        var result = OuroA2UI.McpAppToolResult("A list", shop, messages);
        Assert.Equal(shop.CatalogId, result["structuredContent"]!["a2ui"]!["catalogId"]!.GetValue<string>());
        Assert.Throws<InvalidOperationException>(() => OuroA2UI.McpAppToolResult("x", shop, JsonNode.Parse("""[{ "version": "1.0" }]""")));
    }
}
