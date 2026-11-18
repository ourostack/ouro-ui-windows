using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Ouro.Actions;
using Xunit;

namespace Ouro.Tests;

/// <summary>
/// Tree updates (core v0.19.0's applyTreeUpdate, decision 2026-11-18-composed-tree-updates.md) against core's own update
/// cases, vendored from the tag (core-provenance.json), and core's test/composed.test.mts update tests; and the rule web's
/// ComposedSurface keeps the person's answers by across an update.
/// </summary>
public class ComposedUpdateTests
{
    private static string Root([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    private static JsonNode Read(string path) => JsonNode.Parse(File.ReadAllText(Path.Combine(Root(), path)))!;

    private static readonly OuroComposedCatalog Hx = OuroComposedCatalog.Load(Read("vendor/core/a2ui/catalog.json"));
    private static readonly OuroComposedCatalog App = OuroComposedCatalog.Load(Read("vendor/core/a2ui/conformance/recipe.catalog.json"));

    /// <summary>The update cases core-provenance.json records, so each is core's at the tag, byte for byte.</summary>
    private static readonly string[] CaseNames = Read("core-provenance.json")["files"]!.AsObject().Select(f => f.Key)
        .Where(f => f.StartsWith("vendor/core/composed/updates/", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();

    public static TheoryData<string> Cases() => new(CaseNames.Select(n => Path.GetFileName(n)));

    private static OuroComposedCatalog CatalogOf(JsonObject c) => c["catalog"]!.GetValue<string>() == "catalog.json" ? Hx : App;

    private static JsonObject Case(string name) => Read($"vendor/core/composed/updates/{name}").AsObject();

    private static JsonObject Plan => (JsonObject)Case("replace-and-set.json")["tree"]!.DeepClone();

    private static JsonObject Heading(string id, string text) => Node(id, "Heading", new JsonObject { ["text"] = text, ["level"] = 2 });

    private static JsonObject Node(string id, string component, JsonObject props, params JsonObject[] children) =>
        new() { ["id"] = id, ["component"] = component, ["props"] = props, ["children"] = new JsonArray(children) };

    private static JsonObject Card(params JsonObject[] children) => Node("root", "Card", new JsonObject(), children);

    private static string[] Paths(OuroTreeUpdateResult r) => r.Problems.Select(p => p.Path).ToArray();

    private static JsonObject Update(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void Core_ships_the_five_update_cases_and_they_are_vendored() =>
        Assert.Equal(["invalid-update-missing-id.json", "invalid-update-relabel-button.json", "invalid-update-shared-id.json", "invalid-update-unsafe-pointer.json", "replace-and-set.json"], CaseNames.Select(n => Path.GetFileName(n)));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Each_update_case_gives_what_its_expect_says_and_changes_neither_input(string name)
    {
        var c = Case(name);
        var (tree, data, update) = (c["tree"]!.DeepClone(), c["data"]?.DeepClone(), c["update"]!.DeepClone());
        var result = OuroComposed.ApplyTreeUpdate(CatalogOf(c), tree, data, update);
        var expect = c["expect"]!.AsObject();
        Assert.Equal(name.StartsWith("invalid-", StringComparison.Ordinal), !result.Applied);
        if (expect["problems"] is JsonArray problems)
        {
            Assert.Equal(problems.Select(p => (p!["path"]!.GetValue<string>(), p["message"]!.GetValue<string>())), result.Problems.Select(p => (p.Path, p.Message)));
            Assert.Null(result.Tree);
        }
        else
        {
            Assert.True(JsonNode.DeepEquals(expect["tree"], result.Tree), $"{name}: {result.Tree?.ToJsonString()}");
            Assert.True(JsonNode.DeepEquals(expect["data"], result.Data), $"{name}: {result.Data?.ToJsonString()}");
            Assert.Empty(OuroComposed.ValidateTree(CatalogOf(c), result.Tree));
        }
        Assert.True(JsonNode.DeepEquals(c["tree"], tree) && JsonNode.DeepEquals(c["data"], data) && JsonNode.DeepEquals(c["update"], update), $"{name} changed an input");
    }

    [Fact]
    public void An_update_replaces_the_one_node_with_its_id_in_order_and_refuses_an_id_the_tree_lacks_or_instances_share()
    {
        var tree = Card(Node("box", "Card", new JsonObject(), Heading("a", "A")));
        var nested = OuroComposed.ApplyTreeUpdate(Hx, tree, null, new JsonObject { ["nodes"] = new JsonArray(Node("box", "Card", new JsonObject(), Heading("b", "B")), Heading("b", "B2")) });
        Assert.True(JsonNode.DeepEquals(Card(Node("box", "Card", new JsonObject(), Heading("b", "B2"))), nested.Tree), "each node applies to the tree the ones before it left");
        Assert.Null(nested.Data);
        Assert.Equal(["/nodes/1/id"], Paths(OuroComposed.ApplyTreeUpdate(Hx, tree, null, new JsonObject { ["nodes"] = new JsonArray(Heading("box", "Box"), Heading("a", "A2")) })));
        Assert.True(JsonNode.DeepEquals(Card(), OuroComposed.ApplyTreeUpdate(Hx, tree, null, new JsonObject { ["nodes"] = new JsonArray(Card()) }).Tree), "the root is a node like any other");
        var shared = Card(Heading("day", "Monday"), Heading("day", "Tuesday"));
        Assert.Matches("names 2 nodes, a template's instances; replace the node that holds them", OuroComposed.ApplyTreeUpdate(Hx, shared, null, new JsonObject { ["nodes"] = new JsonArray(Heading("day", "Wednesday")) }).Problems[0].Message);
        foreach (var id in new[] { "c", "__proto__", "constructor", "toString" })
            Assert.Matches("is not in the tree", OuroComposed.ApplyTreeUpdate(Hx, tree, null, new JsonObject { ["nodes"] = new JsonArray(Heading(id, "X")) }).Problems[0].Message);
    }

    [Fact]
    public void An_update_cant_relabel_a_button_loosen_its_consent_or_forge_its_confirm_sheet_and_a_refused_one_applies_nothing()
    {
        var buttons = Read("vendor/core/a2ui/conformance/cases/action-buttons.json")["expect"]!["surfaces"]!["buttons"]!["children"]!.AsArray();
        JsonObject Button(int i) => (JsonObject)buttons[i]!.DeepClone();
        var tree = Card(Button(1), Button(2));
        var loosened = Button(2);
        loosened["props"]!["press"] = "run";
        Assert.Contains("/nodes/0/props/press", Paths(OuroComposed.ApplyTreeUpdate(App, tree, new JsonObject(), new JsonObject { ["nodes"] = new JsonArray(loosened) })));
        var forged = Button(2);
        forged["props"]!["confirm"]!["fields"]![1]!["value"] = "friend@example.com";
        Assert.Matches("confirm sheet shows a row", OuroComposed.ApplyTreeUpdate(App, tree, new JsonObject(), new JsonObject { ["nodes"] = new JsonArray(forged) }).Problems[0].Message);
        var relabeled = Button(1);
        relabeled["props"]!["label"] = "Buy now";
        var mixed = OuroComposed.ApplyTreeUpdate(App, tree, Update("""{"servings":4}"""), new JsonObject { ["nodes"] = new JsonArray(relabeled), ["data"] = JsonNode.Parse("""[{"path":"/servings","value":6}]""") });
        Assert.False(mixed.Applied);
        Assert.Null(mixed.Data);
        Assert.Matches("not in this catalog", OuroComposed.ApplyTreeUpdate(App, tree, new JsonObject(), new JsonObject { ["nodes"] = new JsonArray(Node("shop", "Button", new JsonObject())) }).Problems[0].Message);
        Assert.Equal(["/children/0/props/label"], Paths(OuroComposed.ApplyTreeUpdate(App, Card((JsonObject)relabeled.DeepClone()), new JsonObject(), Update("""{"data":[{"path":"/x","value":1}]}"""))));
    }

    [Fact]
    public void An_updates_data_path_stays_in_the_data_a_list_takes_only_an_index_and_the_data_model_stays_an_object()
    {
        foreach (var path in new[] { "/__proto__/polluted", "/constructor/prototype/polluted", "/a/__proto__", "servings", "" })
        {
            var update = new JsonObject { ["data"] = new JsonArray(new JsonObject { ["path"] = path, ["value"] = true }) };
            Assert.Equal(["/data/0/path"], Paths(OuroComposed.ApplyTreeUpdate(App, Plan, new JsonObject(), update)));
        }
        var list = Update("""{"steps":["a","b"]}""");
        var grown = OuroComposed.ApplyTreeUpdate(App, Plan, list, Update("""{"data":[{"path":"/steps/2","value":"c"},{"path":"/steps/0","value":null}]}"""));
        Assert.Equal("""{"steps":["b","c"]}""", grown.Data!.ToJsonString());
        Assert.True(JsonNode.DeepEquals(Plan, grown.Tree));
        Assert.Matches("an index from 0 to 2", OuroComposed.ApplyTreeUpdate(App, Plan, list, Update("""{"data":[{"path":"/steps/5","value":"c"}]}""")).Problems[0].Message);
        Assert.Matches("an index from 0 to 2", OuroComposed.ApplyTreeUpdate(App, Plan, list, Update("""{"data":[{"path":"/steps/first","value":"c"}]}""")).Problems[0].Message);
        Assert.Equal("""{"servings":2}""", OuroComposed.ApplyTreeUpdate(App, Plan, list, Update("""{"data":[{"path":"/","value":{"servings":2}}]}""")).Data!.ToJsonString());
        Assert.Matches("The data model is an object", OuroComposed.ApplyTreeUpdate(App, Plan, list, Update("""{"data":[{"path":"/","value":5}]}""")).Problems[0].Message);
        Assert.Equal("""{"servings":6}""", OuroComposed.ApplyTreeUpdate(App, Plan, null, Update("""{"data":[{"path":"/servings","value":6}]}""")).Data!.ToJsonString());
        Assert.Equal(["/data/0"], Paths(OuroComposed.ApplyTreeUpdate(App, Plan, new JsonObject(), Update("""{"data":[{"path":"/x"}]}"""))));
    }

    [Fact]
    public void An_update_is_nodes_and_data_JSON_all_the_way_down_and_its_result_shares_nothing_with_its_inputs()
    {
        Assert.Equal(["/tree"], Paths(OuroComposed.ApplyTreeUpdate(App, Plan, new JsonObject(), new JsonObject { ["tree"] = Plan })));
        Assert.Equal([""], Paths(OuroComposed.ApplyTreeUpdate(App, Plan, new JsonObject(), null)));
        Assert.Equal(["/nodes", "/data"], Paths(OuroComposed.ApplyTreeUpdate(App, Plan, new JsonObject(), Update("""{"nodes":{},"data":"x"}"""))));
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity })
        {
            var update = new JsonObject { ["data"] = new JsonArray(new JsonObject { ["path"] = "/x", ["value"] = new JsonObject { ["a/b"] = bad } }, new JsonObject { ["path"] = "/y", ["value"] = 1 }) };
            Assert.Equal(["/data/0/value/a~1b"], Paths(OuroComposed.ApplyTreeUpdate(App, Plan, new JsonObject(), update)));
        }
        Assert.Equal([""], Paths(OuroComposed.ApplyTreeUpdate(App, Plan, new JsonObject(), JsonNode.Parse("""{"data":[],"data":[]}"""))));
        var node = Node("title", "Heading", new JsonObject { ["text"] = "New", ["level"] = 1 });
        var data = Update("""{"servings":{"count":4}}""");
        var result = OuroComposed.ApplyTreeUpdate(App, Plan, data, new JsonObject { ["nodes"] = new JsonArray(node) });
        result.Tree!["children"]![0]!["props"]!["text"] = "Changed";
        result.Data!["servings"]!["count"] = 9;
        Assert.Equal("New", node["props"]!["text"]!.GetValue<string>());
        Assert.Equal(4, data["servings"]!["count"]!.GetValue<int>());
        Assert.Empty(OuroComposed.TreeUpdateProblems(App, Update("""{"nodes":[],"data":[{"path":"/servings","value":null}]}""")));
        foreach (var broken in new[] { """{"id":"root","component":"Card","props":{}}""", """{"id":"root","component":"Card","props":{},"children":[null]}""", "[]" })
            Assert.NotEmpty(OuroComposed.ApplyTreeUpdate(App, JsonNode.Parse(broken), null, new JsonObject { ["nodes"] = new JsonArray(Heading("root", "X")) }).Problems);
    }

    [Fact]
    public void A_tool_result_carries_an_update_in_place_of_a_tree_checked_against_the_catalog()
    {
        JsonNode Result(string ouro) => JsonNode.Parse("""{"content":[{"type":"text","text":"Six servings"}],"structuredContent":{"ouro":""" + ouro + "}}")!;
        var ok = OuroComposedResult.From(Result($$$"""{"version":"1","catalogId":"{{{App.CatalogId}}}","update":{"data":[{"path":"/servings","value":6}]}}"""))!;
        Assert.NotNull(ok.Update);
        Assert.Null(ok.Tree);
        Assert.Empty(ok.Problems(App));
        var unsafePath = OuroComposedResult.From(Result($$$"""{"version":"1","catalogId":"{{{App.CatalogId}}}","update":{"data":[{"path":"/__proto__/x","value":1}]}}"""))!;
        Assert.Equal(["/structuredContent/ouro/update/data/0/path"], unsafePath.Problems(App).Select(p => p.Path));
        var both = OuroComposedResult.From(Result($$$"""{"version":"1","catalogId":"{{{App.CatalogId}}}","tree":{{{Plan.ToJsonString()}}},"update":{}}"""))!;
        Assert.Equal(["/structuredContent/ouro"], both.Problems(App).Select(p => p.Path));
        Assert.Equal(["/structuredContent/ouro/update"], OuroComposedResult.From(Result($$$"""{"version":"1","catalogId":"{{{App.CatalogId}}}","update":null}"""))!.Problems(App).Select(p => p.Path));
    }

    [Fact]
    public void Ouro_Shops_agent_updates_its_card_and_an_update_for_a_node_the_card_lacks_is_refused()
    {
        var catalog = OuroComposedCatalog.Load(Read("samples/OuroShop/Composed/catalog.json"));
        var card = OuroComposedResult.From(Read("samples/OuroShop/Composed/card.json"))!;
        var update = OuroComposedResult.From(Read("samples/OuroShop/Composed/update.json"))!;
        Assert.Empty(update.Problems(catalog));
        var applied = OuroComposed.ApplyTreeUpdate(catalog, card.Tree, card.Data, update.Update);
        Assert.True(applied.Applied, string.Join("; ", applied.Problems));
        Assert.Equal(6, applied.Data!["quantity"]!.GetValue<int>());
        var missing = OuroComposedResult.From(Read("samples/OuroShop/Composed/update-missing.json"))!;
        Assert.Empty(missing.Problems(catalog));
        Assert.Equal(["/nodes/0/id"], Paths(OuroComposed.ApplyTreeUpdate(catalog, card.Tree, card.Data, missing.Update)));
    }

    [Fact]
    public void The_persons_answers_stay_across_an_update_except_where_the_agent_changed_the_value_or_its_list_item()
    {
        var before = Update("""{"servings":4,"note":"","share":{"with":""},"items":[{"name":"orzo","qty":1},{"name":"lemon","qty":2}]}""");
        var current = Update("""{"servings":6,"note":"no nuts","share":{"with":"pat@example.com"},"items":[{"name":"orzo","qty":3},{"name":"lemon","qty":5}]}""");
        string[] edited = ["/servings", "/note", "/share/with", "/items/0/qty", "/items/1/qty"];
        // The agent sets servings, and changes the first item's name: its qty, which the agent left, goes with the item.
        var after = Update("""{"servings":8,"note":"","share":{"with":""},"items":[{"name":"rice","qty":1},{"name":"lemon","qty":2}]}""");
        var (data, kept) = OuroComposed.KeepAnswers(before, after, current, edited);
        Assert.Equal("""{"servings":8,"note":"no nuts","share":{"with":"pat@example.com"},"items":[{"name":"rice","qty":1},{"name":"lemon","qty":5}]}""", data.ToJsonString());
        Assert.Equal(["/items/1/qty", "/note", "/share/with"], kept.Order(StringComparer.Ordinal));
        // A list that loses an item moves the next into its place: the moved item counts as changed, so its answer goes.
        var shifted = Update("""{"servings":4,"note":"","share":{"with":""},"items":[{"name":"lemon","qty":2}]}""");
        Assert.Equal("""[{"name":"lemon","qty":2}]""", OuroComposed.KeepAnswers(before, shifted, current, edited).Data["items"]!.ToJsonString());
        // An answer the person cleared stays cleared.
        var cleared = Update("""{"servings":4}""");
        Assert.False(OuroComposed.KeepAnswers(cleared, Update("""{"servings":4}"""), Update("{}"), ["/servings"]).Data.ContainsKey("servings"));
    }
}
