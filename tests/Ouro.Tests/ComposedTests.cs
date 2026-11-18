using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Ouro.Actions;
using Xunit;

namespace Ouro.Tests;

/// <summary>
/// The C# port of core's composed tree (src/Ouro.Actions/Composed.cs) against core's own conformance cases and catalogs,
/// vendored from the core tag (core-provenance.json): every valid case's trees decode and validate, forged ones don't,
/// and a press carries what the person entered. Core's test/composed.test.mts is the model for each case here.
/// </summary>
public class ComposedTests
{
    private static string Root([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    private static JsonNode Read(string path) => JsonNode.Parse(File.ReadAllText(Path.Combine(Root(), path)))!;

    private static readonly OuroComposedCatalog Hx = OuroComposedCatalog.Load(Read("vendor/core/a2ui/catalog.json"));
    private static readonly OuroComposedCatalog App = OuroComposedCatalog.Load(Read("vendor/core/a2ui/conformance/recipe.catalog.json"));

    /// <summary>The cases core-provenance.json records, so every one is core's at the tag, byte for byte.</summary>
    private static readonly (string Name, JsonObject Case)[] Cases = Read("core-provenance.json")["files"]!.AsObject().Select(f => f.Key)
        .Where(f => f.StartsWith("vendor/core/a2ui/conformance/cases/", StringComparison.Ordinal)).Order(StringComparer.Ordinal)
        .Select(f => (Path.GetFileName(f), Read(f).AsObject())).ToArray();

    private static OuroComposedCatalog CatalogOf(JsonObject c) => c["catalog"]!.GetValue<string>() == "catalog.json" ? Hx : App;

    private static IEnumerable<(string Name, string Surface, JsonObject Tree, OuroComposedCatalog Catalog)> Trees() =>
        from c in Cases
        where !c.Name.StartsWith("invalid-", StringComparison.Ordinal)
        from s in c.Case["expect"]!["surfaces"]!.AsObject()
        where s.Value is JsonObject
        select (c.Name, s.Key, s.Value!.AsObject(), CatalogOf(c.Case));

    private static JsonObject Surface(string name, string surface) => Cases.Single(c => c.Name == name).Case["expect"]!["surfaces"]![surface]!.AsObject();

    private static JsonObject Buttons => Surface("action-buttons.json", "buttons");

    private static JsonObject Child(JsonObject tree, int i) => (JsonObject)tree["children"]![i]!.DeepClone();

    private static JsonObject Card(params JsonObject[] children) => new() { ["id"] = "root", ["component"] = "Card", ["props"] = new JsonObject(), ["children"] = new JsonArray(children) };

    private static JsonObject Node(string component, string props, params JsonObject[] children) =>
        new() { ["id"] = "root", ["component"] = component, ["props"] = JsonNode.Parse(props), ["children"] = new JsonArray(children) };

    private static OuroComposedNode Find(OuroComposedNode n, string id) => n.Id == id ? n : n.Children.Select(c => FindOrNull(c, id)).First(x => x is not null)!;

    private static OuroComposedNode? FindOrNull(OuroComposedNode n, string id) => n.Id == id ? n : n.Children.Select(c => FindOrNull(c, id)).FirstOrDefault(x => x is not null);

    private static IEnumerable<OuroComposedNode> All(OuroComposedNode n) => n.Children.SelectMany(All).Prepend(n);

    [Fact]
    public void Every_valid_conformance_tree_decodes_validates_and_round_trips()
    {
        var trees = Trees().ToArray();
        Assert.True(trees.Select(t => t.Name).Distinct().Count() >= 6, $"only {trees.Length} trees");
        foreach (var (name, surface, tree, catalog) in trees)
        {
            Assert.True(OuroComposed.ValidateTree(catalog, tree).Count == 0, $"{name} {surface}: {string.Join("; ", OuroComposed.ValidateTree(catalog, tree))}");
            var node = OuroComposedNode.FromJson(tree);
            Assert.True(JsonNode.DeepEquals(tree, node.ToJson()), $"{name} {surface} round-trips");
            foreach (var button in All(node).Where(n => n.Component == "ActionButton"))
            {
                switch (button.Text("press"))
                {
                    case "handoff":
                        Assert.Empty(button.Props["input"]!.AsObject());
                        Assert.Throws<InvalidOperationException>(() => OuroComposed.PersonPress(button, true));
                        break;
                    case "confirm":
                        Assert.Throws<InvalidOperationException>(() => OuroComposed.PersonPress(button));
                        Assert.True(OuroComposed.PersonPress(button, true).Confirmed);
                        break;
                    default:
                        Assert.False(OuroComposed.PersonPress(button).Confirmed);
                        break;
                }
            }
        }
    }

    [Fact]
    public void The_port_draws_cores_components_with_cores_props_and_no_more()
    {
        var specs = Read("vendor/core/a2ui/catalog.json")["components"]!.AsObject().ToList();
        specs.Add(new("ActionButton", Read("vendor/core/a2ui/conformance/recipe.catalog.json")["components"]!["ActionButton"]!.DeepClone()));
        Assert.Equal(OuroComposed.Components.Keys.Append("ActionButton"), specs.Select(s => s.Key));
        foreach (var (name, json) in specs)
        {
            var spec = name == "ActionButton" ? OuroComposed.ActionButton : OuroComposed.Components[name];
            Assert.Equal(json!["description"]!.GetValue<string>(), spec.Description);
            var props = json["properties"]!.AsObject().Where(p => p.Key != "component").ToArray();
            Assert.Equal(props.Select(p => p.Key), spec.Props.Keys);
            var required = json["required"]!.AsArray().Select(r => r!.GetValue<string>()).Where(r => r != "component").ToHashSet();
            foreach (var (key, p) in props)
            {
                var mine = spec.Props[key];
                Assert.Equal(p!["description"]!.GetValue<string>(), mine.Description);
                Assert.Equal(required.Contains(key), mine.Required);
                if (p["enum"] is JsonArray values && mine.Kind == OuroPropKind.Enum) Assert.Equal(values.Select(v => v!.GetValue<string>()), mine.Values!);
                if (p["default"] is JsonNode d) Assert.Equal(d.ToJsonString(), JsonValue.Create(mine.Default)!.ToJsonString());
            }
        }
        Assert.Equal(["TextField", "Checkbox", "Select"], OuroComposed.InputComponents);
    }

    [Fact]
    public void A_tree_cant_relabel_a_button_loosen_its_consent_or_drop_its_confirm_sheet()
    {
        var share = Child(Buttons, 2);
        var relabeled = (JsonObject)share.DeepClone();
        relabeled["props"]!["label"] = "Add to list";
        Assert.Matches("share_recipe's label is the catalog's \"Share recipe\", never the sender's", OuroComposed.ValidateTree(App, Card(relabeled))[0].Message);

        var loosened = (JsonObject)share.DeepClone();
        loosened["props"]!["press"] = "run";
        loosened["props"]!["consent"] = "write";
        var problems = OuroComposed.ValidateTree(App, Card(loosened));
        Assert.Contains(problems, p => p.Path == "/children/0/props/press");
        Assert.Contains(problems, p => p.Path == "/children/0/props/consent");

        var unconfirmed = (JsonObject)share.DeepClone();
        unconfirmed["props"]!.AsObject().Remove("confirm");
        Assert.Matches("confirms, with the catalog's title", OuroComposed.ValidateTree(App, Card(unconfirmed))[0].Message);

        var confirmsWrite = Child(Buttons, 1);
        confirmsWrite["props"]!["confirm"] = share["props"]!["confirm"]!.DeepClone();
        Assert.Matches("doesn't confirm", OuroComposed.ValidateTree(App, Card(confirmsWrite))[0].Message);

        var ordered = Child(Buttons, 3);
        ordered["props"]!["input"] = new JsonObject { ["recipe"] = "rcp_123" };
        var refused = OuroComposed.ValidateTree(App, Card(ordered))[0];
        Assert.Equal("person-only", refused.Refusal!["error"]!["code"]!.GetValue<string>());
        Assert.Equal(App.Actions["order_ingredients"].PersonOnly!.Refusal["summary"]!.GetValue<string>(), refused.Message);

        var handoffDropped = Child(Buttons, 3);
        handoffDropped["props"]!.AsObject().Remove("handoff");
        Assert.Contains(OuroComposed.ValidateTree(App, Card(handoffDropped)), p => p.Path == "/children/0/props/handoff");

        var unknown = Child(Buttons, 0);
        unknown["props"]!["action"] = "pay";
        Assert.Matches("not one of the app's actions", OuroComposed.ValidateTree(App, Card(unknown))[0].Message);
    }

    [Fact]
    public void A_confirm_sheet_cant_show_the_person_one_value_and_send_another()
    {
        var share = Child(Buttons, 2);
        JsonArray Rows(JsonObject button) => button["props"]!["confirm"]!["fields"]!.AsArray();
        string First(JsonObject tree) => OuroComposed.ValidateTree(App, Card(tree)) is { Count: > 0 } p ? p[0].Message : "";

        var forged = (JsonObject)share.DeepClone();
        Rows(forged)[^1]!["value"] = "friend@example.com";
        Assert.StartsWith("share_recipe's confirm sheet shows a row for each prepared input", First(forged));

        var dropped = (JsonObject)share.DeepClone();
        Rows(dropped).RemoveAt(Rows(dropped).Count - 1);
        Assert.StartsWith("share_recipe's confirm sheet shows a row", First(dropped));

        var relabeled = (JsonObject)share.DeepClone();
        Rows(relabeled)[0]!["label"] = "Dinner";
        Assert.StartsWith("share_recipe's confirm sheet shows a row", First(relabeled));

        var named = (JsonObject)share.DeepClone();
        Rows(named)[0]!["raw"] = Rows(named)[0]!["value"]!.GetValue<string>();
        Rows(named)[0]!["value"] = "My lemon orzo";
        Assert.Empty(OuroComposed.ValidateTree(App, Card(named)));

        var hidden = (JsonObject)share.DeepClone();
        Rows(hidden)[1]!["raw"] = Rows(hidden)[1]!["value"]!.GetValue<string>();
        Rows(hidden)[1]!["value"] = "friend@example.com";
        Assert.StartsWith("share_recipe's confirm sheet shows a row", First(hidden));

        var extra = (JsonObject)share.DeepClone();
        extra["props"]!["input"]!["everyone"] = true;
        var problem = OuroComposed.ValidateTree(App, Card(extra))[0];
        Assert.Equal("/children/0/props/input/everyone", problem.Path);
        Assert.StartsWith("share_recipe has no input everyone", problem.Message);

        var unbound = OuroComposedNode.FromJson(Surface("inputs.json", "plan")).Children.Single(n => n.Id == "actions").Children.Single(n => n.Id == "share").ToJson();
        Rows(unbound)[1]!.AsObject().Remove("bind");
        Assert.StartsWith("share_recipe's confirm sheet shows a row", First(unbound));
    }

    [Theory]
    [InlineData("constructor")]
    [InlineData("toString")]
    [InlineData("__proto__")]
    [InlineData("hasOwnProperty")]
    public void A_name_a_sender_chooses_is_the_catalogs_only_when_the_catalog_itself_has_it(string name)
    {
        var extra = Child(Buttons, 2);
        extra["props"]!["input"] = JsonNode.Parse($$"""{"recipe":"rcp_123","with":"sam@example.com",{{JsonValue.Create(name).ToJsonString()}}:true}""");
        Assert.Contains(OuroComposed.ValidateTree(App, Card(extra)), p => p.Message.StartsWith($"share_recipe has no input {name}", StringComparison.Ordinal));
        Assert.Matches("is not in this catalog", OuroComposed.ValidateTree(App, Card(new JsonObject { ["id"] = "x", ["component"] = name, ["props"] = new JsonObject(), ["children"] = new JsonArray() }))[0].Message);
        var button = Child(Buttons, 0);
        button["props"]!["action"] = name;
        Assert.Matches("not one of the app's actions", OuroComposed.ValidateTree(App, Card(button))[0].Message);
        var catalog = JsonNode.Parse("""{"catalogId":"x","components":{"Text":{}}}""")!;
        catalog["components"]![name] = new JsonObject();
        Assert.Matches($"component {System.Text.RegularExpressions.Regex.Escape(name)} is not Ouro HX's", Assert.Throws<InvalidDataException>(() => OuroComposedCatalog.Load(catalog)).Message);
    }

    [Fact]
    public void A_pointer_never_names_an_objects_prototype_writes_through_one_write_nothing_and_the_validator_refuses_it()
    {
        foreach (var p in new[] { "/__proto__/polluted", "/constructor/prototype/polluted", "/a/__proto__/toString", "/a/prototype" })
        {
            Assert.Equal("""{"a":{}}""", OuroPointer.Set(JsonNode.Parse("""{"a":{}}"""), p, "yes").ToJsonString());
            Assert.Equal("""{"a":{"prototype":1}}""", OuroPointer.Set(JsonNode.Parse("""{"a":{"prototype":1}}"""), p, null).ToJsonString());
            Assert.False(OuroPointer.IsData(p), p);
        }
        Assert.False(OuroPointer.IsSafe("constructor"));
        Assert.False(OuroPointer.IsSafe("items/__proto__"));
        Assert.True(OuroPointer.IsData("/servings"));
        Assert.Null(OuroPointer.Get(JsonNode.Parse("""{"a":1}"""), "/toString"));
        Assert.Null(OuroPointer.Get(JsonNode.Parse("""{"a":1}"""), "/__proto__"));
        var field = Card(Node("TextField", """{"label":"X","value":"/__proto__/x","type":"text","multiline":false}"""));
        Assert.Matches("JSON pointer into the data model", OuroComposed.ValidateTree(Hx, field)[0].Message);
        var bound = Child(Buttons, 1);
        bound["props"]!["input"] = JsonNode.Parse("""{"recipe":"rcp_123","servings":{"@path":"/__proto__/servings"}}""");
        Assert.Contains(OuroComposed.ValidateTree(App, Card(bound)), p => p.Path == "/children/0/props/input/servings");
    }

    [Fact]
    public void The_apps_label_resolver_names_only_an_id_and_every_other_row_shows_what_is_sent()
    {
        var asked = new List<string>();
        var props = OuroComposed.ActionButtonProps(App, "share_recipe", new JsonObject { ["recipe"] = "rcp_123", ["with"] = "sam@example.com" }, ctx =>
        {
            asked.Add(ctx.Field);
            return "A name";
        });
        Assert.Equal(["recipe"], asked);
        Assert.Equal("""[{"label":"Recipe","value":"A name","raw":"rcp_123"},{"label":"Send to","value":"sam@example.com"}]""", OuroJson.Write(props["confirm"]!["fields"]!));
    }

    [Fact]
    public void A_tree_names_only_the_catalogs_components_and_their_props_resolved()
    {
        Assert.Matches("not in this catalog", OuroComposed.ValidateTree(Hx, Node("Button", "{}"))[0].Message);
        Assert.Matches("text is text", OuroComposed.ValidateTree(Hx, Node("Text", """{"text":{"@path":"/x"},"tone":"default"}"""))[0].Message);
        Assert.Matches("one of neutral, info", OuroComposed.ValidateTree(Hx, Node("Badge", """{"text":"Hi","tone":"loud"}"""))[0].Message);
        Assert.Matches("Text has no colour", OuroComposed.ValidateTree(Hx, Node("Text", """{"text":"Hi","tone":"default","colour":"red"}"""))[0].Message);
        Assert.Matches("needs description", OuroComposed.ValidateTree(Hx, Node("Image", """{"url":"https://x/y.jpg","aspect":"square"}"""))[0].Message);
        Assert.Matches("Separator has no children", OuroComposed.ValidateTree(Hx, Node("Separator", "{}", Node("Separator", "{}")))[0].Message);
        Assert.Matches("1, 2 or 3", OuroComposed.ValidateTree(Hx, Node("Heading", """{"text":"Hi","level":4}"""))[0].Message);
        Assert.Matches("A node is", OuroComposed.ValidateTree(Hx, JsonNode.Parse("""{"id":"root","component":"Card"}"""))[0].Message);
        Assert.Matches("not in this catalog", OuroComposed.ValidateTree(Hx, Card(Node("ActionButton", "{}")))[0].Message);
        Assert.Throws<InvalidDataException>(() => OuroComposedCatalog.Load(JsonNode.Parse("""{"catalogId":"x","components":{"Button":{}}}""")));
    }

    [Fact]
    public void A_press_is_the_action_the_input_and_that_the_person_pressed_it_and_a_confirm_comes_only_after_they_confirm()
    {
        var buttons = OuroComposedNode.FromJson(Buttons);
        var press = OuroComposed.PersonPress(buttons.Children[1]);
        Assert.Equal("""{"action":"add_recipe_to_shopping_list","input":{"recipe":"rcp_123","servings":4},"consent":"write","label":"Add ingredients to list","by":"person"}""", OuroJson.Write(press.ToJson()));
        Assert.Matches("only after the person confirms", Assert.Throws<InvalidOperationException>(() => OuroComposed.PersonPress(buttons.Children[2])).Message);
        Assert.True(OuroComposed.PersonPress(buttons.Children[2], true).ToJson()["confirmed"]!.GetValue<bool>());
        Assert.Matches("person-only", Assert.Throws<InvalidOperationException>(() => OuroComposed.PersonPress(buttons.Children[3], true)).Message);
    }

    [Fact]
    public void A_field_writes_the_persons_answer_into_the_data_and_a_button_bound_to_it_sends_that_answer()
    {
        var c = Cases.Single(x => x.Name == "inputs.json").Case;
        var plan = OuroComposedNode.FromJson(Surface("inputs.json", "plan"));
        foreach (var name in OuroComposed.InputComponents) Assert.Contains(All(plan), n => n.Component == name);
        var data = c["messages"]![0]!["createSurface"]!["dataModel"]!.DeepClone();
        Assert.Equal(4, OuroPointer.Get(data, Find(plan, "servings").Text("value"))!.GetValue<int>());
        var before = data.ToJsonString();
        data = OuroPointer.Set(data, "/servings", 6);
        data = OuroPointer.Set(data, "/share/with", "sam@example.com");
        Assert.NotEqual(before, data.ToJsonString());
        Assert.Equal("""{"recipe":"rcp_lemon_orzo","servings":6}""", OuroJson.Write(OuroComposed.PersonPress(Find(plan, "shop"), false, data).Input));
        var sheet = OuroComposed.ResolveButton(Find(plan, "share"), data).Props["confirm"]!["fields"]!;
        Assert.Equal("""[{"label":"Recipe","value":"Lemon orzo","raw":"rcp_lemon_orzo"},{"label":"Send to","value":"sam@example.com"}]""", OuroJson.Write(sheet));
        Assert.Equal("sam@example.com", OuroComposed.PersonPress(Find(plan, "share"), true, data).Input["with"]!.GetValue<string>());
        var cleared = OuroComposed.PersonPress(Find(plan, "share"), true, OuroPointer.Set(data, "/share/with", null));
        Assert.True(cleared.Input.ContainsKey("with") && cleared.Input["with"] is null, "an empty answer is sent as null, and the app's tool refuses it");
        Assert.Same(Find(plan, "title"), OuroComposed.ResolveButton(Find(plan, "title"), data));
    }

    [Fact]
    public void A_fields_value_and_a_bound_input_are_pointers_into_the_data()
    {
        JsonObject Field(string extra) => Card(Node("TextField", $$"""{"label":"Servings","type":"text","multiline":false{{extra}}}"""));
        Assert.Empty(OuroComposed.ValidateTree(Hx, Field(",\"value\":\"/servings\"")));
        Assert.Matches("value is a JSON pointer into the data model", OuroComposed.ValidateTree(Hx, Field(",\"value\":\"servings\""))[0].Message);
        Assert.Matches("TextField needs value", OuroComposed.ValidateTree(Hx, Field(""))[0].Message);
        var bound = Child(Buttons, 1);
        bound["props"]!["input"] = JsonNode.Parse("""{"recipe":"rcp_123","servings":{"@path":"servings"}}""");
        Assert.Matches("""servings is a value, or \{"@path": "/\.\.\."\}""", OuroComposed.ValidateTree(App, Card(bound))[0].Message);
    }

    [Fact]
    public void A_buttons_props_come_from_the_catalog_and_the_agent_only_prepares_the_input()
    {
        Assert.Empty(OuroComposed.ActionButtonProps(App, "order_ingredients", new JsonObject { ["recipe"] = "rcp_123" })["input"]!.AsObject());
        var props = OuroComposed.ActionButtonProps(App, "share_recipe", new JsonObject { ["recipe"] = "rcp_123", ["with"] = "sam@example.com" });
        Assert.Equal("Share recipe", props["label"]!.GetValue<string>());
        Assert.Equal("confirm", props["press"]!.GetValue<string>());
        var named = OuroComposed.ActionButtonProps(App, "share_recipe", new JsonObject { ["recipe"] = "rcp_123", ["with"] = "sam@example.com" }, ctx => ctx.Field == "recipe" ? "Lemon orzo" : null);
        Assert.Equal("""[{"label":"Recipe","value":"Lemon orzo","raw":"rcp_123"},{"label":"Send to","value":"sam@example.com"}]""", OuroJson.Write(named["confirm"]!["fields"]!));
    }

    [Fact]
    public void Pointers_read_and_write_as_RFC_6901_says_and_never_change_the_data_they_were_given()
    {
        var data = JsonNode.Parse("""{"a/b":{"c~d":[10,null,{"e":"f"}]},"n":null}""");
        Assert.Equal(10, OuroPointer.Get(data, "/a~1b/c~0d/0")!.GetValue<int>());
        Assert.Equal("f", OuroPointer.Get(data, "/a~1b/c~0d/2/e")!.GetValue<string>());
        Assert.True(OuroPointer.TryGet(data, "/n", out var nothing) && nothing is null);
        Assert.False(OuroPointer.TryGet(data, "/missing", out _));
        Assert.False(OuroPointer.TryGet(data, "/a~1b/c~0d/9", out _));
        Assert.False(OuroPointer.TryGet(data, "/n/deeper", out _));
        Assert.Same(data, OuroPointer.Get(data, ""));
        var written = OuroPointer.Set(data, "/new/deep/x", 1);
        Assert.Equal(1, OuroPointer.Get(written, "/new/deep/x")!.GetValue<int>());
        Assert.False(OuroPointer.TryGet(data, "/new", out _));
        var removed = OuroPointer.Set(data, "/a~1b/c~0d/1", null);
        Assert.Equal("""[10,{"e":"f"}]""", OuroPointer.Get(removed, "/a~1b/c~0d")!.ToJsonString());
        Assert.Equal("{}", OuroPointer.Set(data, "", null).ToJsonString());
        Assert.Equal("""{"x":true}""", OuroPointer.Set(null, "/x", true).ToJsonString());
        var array = JsonNode.Parse("""{"a":[1]}""");
        Assert.Equal("""{"a":[1,2]}""", OuroPointer.Set(array, "/a/1", 2).ToJsonString());
        Assert.Equal("""{"a":[1]}""", OuroPointer.Set(array, "/a/1000000000", 2).ToJsonString());
        Assert.Equal("""{"a":[1]}""", OuroPointer.Set(array, "/a/1000000000/b", 2).ToJsonString());
    }

    [Theory]
    [InlineData("null", "")]
    [InlineData("4", "4")]
    [InlineData("4.0", "4")]
    [InlineData("-0", "0")]
    [InlineData("2.5", "2.5")]
    [InlineData("1234567890123456", "1234567890123456")]
    [InlineData("4111111111111111", "4111111111111111")]
    [InlineData("0.000001", "0.000001")]
    [InlineData("1e-7", "1e-7")]
    [InlineData("123e20", "1.23e+22")]
    [InlineData("1e21", "1e+21")]
    [InlineData("true", "true")]
    [InlineData("\"text\"", "text")]
    [InlineData("{\"a\":1}", "{\"a\":1}")]
    public void A_value_reads_as_JavaScript_writes_it_so_a_confirm_sheet_shows_what_the_press_sends(string json, string text) =>
        Assert.Equal(text, OuroComposed.AsText(JsonNode.Parse(json)));

    [Fact]
    public void JSON_that_names_a_key_twice_is_refused_and_never_throws()
    {
        var twice = JsonNode.Parse("""{"id":"root","component":"Text","props":{"text":"a","text":"b"},"children":[]}""");
        Assert.Equal(["/props names a key twice. Fix: give each key once."], OuroComposed.ValidateTree(Hx, twice).Select(p => p.ToString()));
        var result = OuroComposedResult.From(JsonNode.Parse("""{"structuredContent":{"ouro":{"version":"1","version":"1"}}}"""))!;
        Assert.Single(result.Problems(Hx));
        Assert.Null(OuroComposedResult.From(JsonNode.Parse("""{"structuredContent":"x"}""")));
        Assert.Null(OuroComposedResult.From(JsonNode.Parse("[]")));
        Assert.Throws<InvalidDataException>(() => OuroComposedCatalog.Load(JsonNode.Parse("""{"catalogId":"x","catalogId":"y","components":{}}""")));
    }

    [Fact]
    public void Ouro_Shops_card_is_a_tool_result_whose_tree_validates_against_its_catalog_and_uses_every_component()
    {
        var catalog = OuroComposedCatalog.Load(Read("samples/OuroShop/Composed/catalog.json"));
        var result = OuroComposedResult.From(Read("samples/OuroShop/Composed/card.json"))!;
        Assert.Empty(result.Problems(catalog));
        var tree = OuroComposedNode.FromJson(result.Tree);
        Assert.Equal(OuroComposed.Components.Keys.Append("ActionButton").Order(), All(tree).Select(n => n.Component).Distinct().Order());
        Assert.Equal(["run", "confirm", "handoff"], All(tree).Where(n => n.Component == "ActionButton").Select(n => n.Text("press")));
        Assert.Equal("oat milk", OuroComposed.PersonPress(Find(tree, "add_item"), false, result.Data).Input["name"]!.GetValue<string>());
        Assert.Null(OuroComposedResult.From(JsonNode.Parse("""{"content":[]}""")));
        var other = result with { CatalogId = "https://example.com/catalog.json", Version = "2" };
        Assert.Equal(["/structuredContent/ouro/version", "/structuredContent/ouro/catalogId"], other.Problems(catalog).Select(p => p.Path));
        Assert.Contains(result.Problems(App), p => p.Path.StartsWith("/structuredContent/ouro/tree", StringComparison.Ordinal));
    }
}
