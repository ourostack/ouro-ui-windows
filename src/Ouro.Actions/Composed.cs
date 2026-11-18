// Ouro's composed tree, ported from ouro-ui-core's composed.mts (vendor/core/ouro-composed.mts): what an agent may put on
// a person's screen, as validated data. It holds HX's catalog components (a catalog file can't widen them), each
// action's button as the catalog says it is, the validator that refuses a relabelled button or a loosened consent, JSON
// Pointer reads and writes into the data model the fields fill in, and what a person's press asks the app to do. A
// renderer (Ouro.UI.Windows' OuroComposedSurface) draws a tree only once ValidateTree accepts it.
// Decisions: core docs/decisions/2026-11-14-composed-ui-own-tree.md and 2026-11-17-composed-fields.md.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ouro.Actions;

/// <summary>A prop's kind: what the validator checks (core's <c>PropKind</c>).</summary>
public enum OuroPropKind
{
    /// <summary>Text.</summary>
    String,
    /// <summary>A list of text.</summary>
    Strings,
    /// <summary>The node's children.</summary>
    Children,
    /// <summary>One of a few words.</summary>
    Enum,
    /// <summary>True or false.</summary>
    Boolean,
    /// <summary>A heading level, 1 to 3.</summary>
    Level,
    /// <summary>A list of <c>{ term, detail }</c> text.</summary>
    Terms,
    /// <summary>An ActionButton's action id.</summary>
    Action,
    /// <summary>An ActionButton's prepared input.</summary>
    Input,
    /// <summary>Where a field writes the person's answer: a JSON pointer into the data model.</summary>
    Path,
}

/// <summary>One prop of a component. <paramref name="Default"/> is a string, bool or int, for the kinds that have one.</summary>
public sealed record OuroComposedProp(string Description, OuroPropKind Kind, bool Required = false, IReadOnlyList<string>? Values = null, object? Default = null);

/// <summary>A catalog component: one line for an agent, and its props in order.</summary>
public sealed record OuroComponentSpec(string Description, IReadOnlyDictionary<string, OuroComposedProp> Props);

/// <summary>What pressing an action's button does: run it, confirm then run it, or hand the person to the app's own screen.</summary>
public enum OuroPress
{
    /// <summary>Read and write actions run at once.</summary>
    Run,
    /// <summary>A consequential action shows what it will send and runs only when the person confirms.</summary>
    Confirm,
    /// <summary>A person-only action takes the person to the app's own screen, and never sends.</summary>
    Handoff,
}

/// <summary>One input field of a catalog action: its label for the person, its kind, and the sibling field that names it.</summary>
public sealed record OuroCatalogField(string Label, string Kind, bool Optional, IReadOnlyList<string>? Values = null, string? ShownAs = null);

/// <summary>Where the person does a person-only action, and the refusal every agent door answers it with.</summary>
public sealed record OuroCatalogPersonOnly(string How, JsonObject Refusal);

/// <summary>An action as the catalog carries it, for a renderer that has only the catalog (core's <c>CatalogAction</c>).</summary>
public sealed record OuroCatalogAction(OuroConsent Consent, OuroPress Press, string Label, string Description, IReadOnlyDictionary<string, OuroCatalogField> Input, OuroCatalogPersonOnly? PersonOnly);

/// <summary>A problem in a composed tree: a JSON pointer into it and one sentence, with the typed refusal when one applies.</summary>
public sealed record OuroTreeProblem(string Path, string Message, JsonObject? Refusal = null)
{
    public override string ToString() => $"{Path} {Message}";
}

/// <summary>What the app is asked to name: one prepared input a confirm sheet shows, whose <c>Value</c> is the prepared id as text (core's <c>LabelContext</c>).</summary>
public sealed record OuroLabelContext(string Action, string Field, string Value, JsonObject Input);

/// <summary>
/// A catalog: its id, its components (always HX's own) and what each action's button does. Read one with
/// <see cref="Load"/> from the catalog JSON core's <c>ouro-a2ui-catalog.mts</c> writes for an app's actions.
/// </summary>
public sealed record OuroComposedCatalog(string CatalogId, IReadOnlyDictionary<string, OuroComponentSpec> Components, IReadOnlyDictionary<string, OuroCatalogAction> Actions)
{
    /// <summary>Reads a catalog core wrote (core's <c>loadCatalog</c>). Its component specs are HX's, so a hand-edited catalog can't widen them.</summary>
    /// <exception cref="InvalidDataException">It isn't a catalog, names a component that isn't HX's, or describes an action wrongly.</exception>
    public static OuroComposedCatalog Load(JsonNode? json)
    {
        if (OuroComposed.Twice(json, "") is { } twice) throw new InvalidDataException($"catalog: {twice}");
        if (json is not JsonObject c || !IsString(c["catalogId"], out var id) || c["components"] is not JsonObject declared)
            throw new InvalidDataException("not an A2UI catalog: it has no catalogId or components. Fix: pass a catalog ouro-a2ui-catalog.mts wrote.");
        var components = new Dictionary<string, OuroComponentSpec>();
        foreach (var (name, _) in declared)
        {
            var spec = name == "ActionButton" ? OuroComposed.ActionButton : OuroComposed.Components.GetValueOrDefault(name);
            components[name] = spec ?? throw new InvalidDataException($"catalog {id}: component {name} is not Ouro HX's. Fix: regenerate it with ouro-a2ui-catalog.mts catalog.");
        }
        var actions = new Dictionary<string, OuroCatalogAction>();
        if (c["metadata"]?["extensions"]?["bot_ouroboros_hx"]?["actions"] is JsonObject declaredActions)
            foreach (var (name, a) in declaredActions) actions[name] = ReadAction(id, name, a);
        return new OuroComposedCatalog(id, components, actions);
    }

    private static OuroCatalogAction ReadAction(string catalog, string name, JsonNode? json)
    {
        InvalidDataException Bad(string what) => new($"catalog {catalog}: action {name} {what}. Fix: regenerate it with ouro-a2ui-catalog.mts catalog.");
        if (json is not JsonObject a) throw Bad("is not an object");
        var consent = IsString(a["consent"], out var cs) ? OuroComposed.ConsentOf(cs) : null;
        var press = IsString(a["press"], out var ps) ? OuroComposed.PressOf(ps) : null;
        if (consent is null || press is null || !IsString(a["label"], out var label) || !IsString(a["description"], out var description) || a["input"] is not JsonObject input)
            throw Bad("needs consent, press, label, description and input");
        var fields = new Dictionary<string, OuroCatalogField>();
        foreach (var (field, f) in input)
        {
            if (f is not JsonObject o || !IsString(o["label"], out var fl) || !IsString(o["kind"], out var kind)) throw Bad($"field {field} needs a label and a kind");
            var values = o["values"] is JsonArray vs ? vs.Select(v => IsString(v, out var s) ? s : throw Bad($"field {field}'s values are text")).ToArray() : null;
            fields[field] = new OuroCatalogField(fl, kind, o["optional"] is JsonValue opt && opt.GetValueKind() == JsonValueKind.True, values, IsString(o["shownAs"], out var shownAs) ? shownAs : null);
        }
        OuroCatalogPersonOnly? personOnly = null;
        if (a["personOnly"] is JsonObject po)
        {
            if (!IsString(po["how"], out var how) || po["refusal"] is not JsonObject refusal) throw Bad("is person-only and needs how and refusal");
            personOnly = new OuroCatalogPersonOnly(how, (JsonObject)refusal.DeepClone());
        }
        if ((consent == OuroConsent.PersonOnly) != (personOnly is not null)) throw Bad("has personOnly exactly when it is person-only");
        return new OuroCatalogAction(consent.Value, press.Value, label, description, fields, personOnly);
    }

    internal static bool IsString(JsonNode? node, out string value)
    {
        value = node is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : "";
        return node is JsonValue s && s.GetValueKind() == JsonValueKind.String;
    }
}

/// <summary>
/// A node of the composed tree: its id (a template's instances share it), the component, its props resolved, and its
/// children (core's <c>ComposedNode</c>).
/// </summary>
public sealed record OuroComposedNode(string Id, string Component, JsonObject Props, IReadOnlyList<OuroComposedNode> Children)
{
    /// <summary>A node from JSON. Validate the tree first (<see cref="OuroComposed.ValidateTree"/>); this checks only its shape.</summary>
    /// <exception cref="InvalidDataException">It isn't <c>{ id, component, props, children }</c>.</exception>
    public static OuroComposedNode FromJson(JsonNode? json)
    {
        if (json is not JsonObject n || !OuroComposedCatalog.IsString(n["id"], out var id) || !OuroComposedCatalog.IsString(n["component"], out var component) || n["props"] is not JsonObject props || n["children"] is not JsonArray children)
            throw new InvalidDataException("A node is { id, component, props, children }.");
        return new OuroComposedNode(id, component, (JsonObject)props.DeepClone(), children.Select(FromJson).ToArray());
    }

    /// <summary>The node as JSON, as core writes it.</summary>
    public JsonObject ToJson() => new()
    {
        ["id"] = Id,
        ["component"] = Component,
        ["props"] = Props.DeepClone(),
        ["children"] = new JsonArray(Children.Select(c => (JsonNode)c.ToJson()).ToArray()),
    };

    /// <summary>A prop as text, or "" when it's missing or not text.</summary>
    public string Text(string prop) => OuroComposedCatalog.IsString(Props[prop], out var s) ? s : "";
}

/// <summary>
/// What pressing a composed button asks the app to do: the action, its input, its consent, its label as the person read
/// it, and whether the person confirmed it, which a consequential action needs (core's <c>PersonPress</c>).
/// </summary>
public sealed record OuroPersonPress(string Action, JsonObject Input, OuroConsent Consent, string Label, bool Confirmed)
{
    /// <summary>Who pressed it: always the person.</summary>
    public string By => "person";

    /// <summary>The press as JSON, as core writes it.</summary>
    public JsonObject ToJson()
    {
        var o = new JsonObject { ["action"] = Action, ["input"] = Input.DeepClone(), ["consent"] = OuroComposed.ConsentName(Consent), ["label"] = Label, ["by"] = By };
        if (Confirmed) o["confirmed"] = true;
        return o;
    }
}

/// <summary>
/// A tool result's composed tree: MCP Apps' <c>structuredContent.ouro</c> (core's <c>mcpAppTreeResult</c>), with the
/// data model its fields start from; or, in place of the tree, an <see cref="Update"/> to the tree a view already draws
/// (core's <c>mcpAppTreeUpdateResult</c>).
/// </summary>
public sealed record OuroComposedResult(string Version, string CatalogId, JsonNode? Tree, JsonObject? Data)
{
    /// <summary>
    /// An update to the tree the view already draws, <c>{ nodes?, data? }</c>, in place of a tree; null for a whole tree.
    /// Apply it with <see cref="OuroComposed.ApplyTreeUpdate"/>, or <c>OuroComposedSurface.Show</c>.
    /// </summary>
    public JsonNode? Update { get; init; }

    /// <summary>The tree or update a tool result carries, or null when it carries neither.</summary>
    public static OuroComposedResult? From(JsonNode? toolResult)
    {
        if (toolResult is not JsonObject result) return null;
        if (OuroComposed.Twice(result, "") is { } twice) return new OuroComposedResult("", "", null, null) { Unreadable = twice };
        if (result["structuredContent"] is not JsonObject content || content["ouro"] is not JsonObject ouro) return null;
        return new OuroComposedResult(
            OuroComposedCatalog.IsString(ouro["version"], out var v) ? v : "",
            OuroComposedCatalog.IsString(ouro["catalogId"], out var c) ? c : "",
            ouro["tree"]?.DeepClone(),
            ouro["data"]?.DeepClone() as JsonObject)
        {
            // "update": null is no update object; an empty list stands for it, so Problems names the shape.
            Update = ouro.ContainsKey("update") ? ouro["update"]?.DeepClone() ?? new JsonArray() : null,
            Unreadable = ouro["data"] is null or JsonObject ? null : new("/structuredContent/ouro/data", "data is an object: the data model the fields start from."),
        };
    }

    private OuroTreeProblem? Unreadable { get; init; }

    /// <summary>
    /// Every reason it can't be drawn with this catalog: JSON that names a key twice, data that isn't an object, another
    /// tree version, another catalog, a tree and an update both, or the tree's or the update's own problems. An update's
    /// ids are checked only against the tree it changes (<see cref="OuroComposed.ApplyTreeUpdate"/>).
    /// </summary>
    public IReadOnlyList<OuroTreeProblem> Problems(OuroComposedCatalog catalog)
    {
        const string at = "/structuredContent/ouro";
        var problems = new List<OuroTreeProblem>();
        if (Unreadable is not null) return [Unreadable];
        if (Version != OuroComposed.Version) problems.Add(new($"{at}/version", $"is \"{Version}\"; this renderer draws version {OuroComposed.Version} of the composed tree."));
        if (CatalogId != catalog.CatalogId) problems.Add(new($"{at}/catalogId", $"is \"{CatalogId}\", and this renderer has {catalog.CatalogId}. Fix: compose against the app's own catalog."));
        if (Update is not null)
        {
            if (Tree is not null) problems.Add(new(at, "carries a tree or an update to the tree the view draws, not both."));
            problems.AddRange(OuroComposed.TreeUpdateProblems(catalog, Update).Select(p => p with { Path = $"{at}/update{p.Path}" }));
            return problems;
        }
        problems.AddRange(OuroComposed.ValidateTree(catalog, Tree, $"{at}/tree"));
        return problems;
    }
}

/// <summary>JSON Pointer (RFC 6901) reads and writes into a composed tree's data model (core's <c>getPointer</c> and <c>setPointer</c>).</summary>
public static class OuroPointer
{
    /// <summary>Whether the pointer reaches a value; <paramref name="value"/> is that value, which may be JSON null.</summary>
    public static bool TryGet(JsonNode? data, string pointer, out JsonNode? value)
    {
        value = data;
        if (pointer is "" or "/") return true;
        foreach (var key in Keys(pointer))
        {
            if (value is JsonArray array)
            {
                if (Index(key) is not { } i || i >= array.Count) { value = null; return false; }
                value = array[i];
            }
            else if (value is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(key, out var next)) { value = null; return false; }
                value = next;
            }
            else
            {
                value = null;
                return false;
            }
        }
        return true;
    }

    /// <summary>What the data holds at the pointer, or null when nothing (or JSON null) is there.</summary>
    public static JsonNode? Get(JsonNode? data, string pointer) => TryGet(data, pointer, out var v) ? v : null;

    /// <summary>
    /// A copy of the data with the value written at the pointer, creating objects on the way; null deletes. The data is left
    /// as it was. An array takes a value at an index it has or the one after its last; a write further on goes nowhere,
    /// so no path can make it grow without end. A pointer with a segment that names an object's prototype in JavaScript
    /// (<c>__proto__</c>, <c>constructor</c>, <c>prototype</c>) writes nothing, as in every other kit.
    /// </summary>
    public static JsonNode Set(JsonNode? data, string pointer, JsonNode? value)
    {
        if (pointer is "" or "/") return value?.DeepClone() ?? new JsonObject();
        var keys = Keys(pointer);
        if (keys.Any(Unsafe.Contains)) return data?.DeepClone() ?? new JsonObject();
        var root = data is JsonObject or JsonArray ? data.DeepClone() : new JsonObject();
        JsonNode cur = root;
        for (var i = 0; i < keys.Length; i++)
        {
            var k = keys[i];
            var index = cur is JsonArray ? Index(k) : null;
            if (i == keys.Length - 1)
            {
                if (cur is JsonObject o)
                {
                    if (value is null) o.Remove(k);
                    else o[k] = value.DeepClone();
                }
                else if (cur is JsonArray a && index is { } at)
                {
                    if (value is null) { if (at < a.Count) a.RemoveAt(at); }
                    else if (at < a.Count) a[at] = value.DeepClone();
                    else if (at == a.Count) a.Add(value.DeepClone());
                }
                break;
            }
            JsonNode? next = cur switch
            {
                JsonObject o => o[k],
                JsonArray a when index is { } at && at < a.Count => a[at],
                _ => null,
            };
            if (next is not (JsonObject or JsonArray))
            {
                next = new JsonObject();
                if (cur is JsonObject o) o[k] = next;
                else if (cur is JsonArray a && index is { } at && at <= a.Count)
                {
                    if (at == a.Count) a.Add(next);
                    else a[at] = next;
                }
            }
            cur = next;
        }
        return root;
    }

    // The segments that would reach an object's prototype in JavaScript (ouro-ui-core#91). C#'s JSON objects have no
    // prototype, but every kit refuses the same pointers, so a tree one kit refuses no other draws.
    private static readonly HashSet<string> Unsafe = ["__proto__", "constructor", "prototype"];

    /// <summary>Whether no segment of a pointer, absolute or relative, is <c>__proto__</c>, <c>constructor</c> or <c>prototype</c> (core's <c>isSafePointer</c>).</summary>
    public static bool IsSafe(string pointer) => !Keys(pointer).Any(Unsafe.Contains);

    /// <summary>Whether a pointer stays in the data: absolute, and safe (core's <c>isDataPointer</c>).</summary>
    public static bool IsData(string pointer) => pointer.StartsWith('/') && IsSafe(pointer);

    private static string[] Keys(string pointer) =>
        (pointer.StartsWith('/') ? pointer[1..] : pointer).Split('/').Select(k => k.Replace("~1", "/").Replace("~0", "~")).ToArray();

    private static int? Index(string key) => key.Length > 0 && key.All(char.IsAsciiDigit) && int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var i) ? i : null;
}

/// <summary>Ouro's composed tree: HX's catalog components, the rules for an action's button, the validator, and what a press sends.</summary>
public static class OuroComposed
{
    /// <summary>The version of the composed tree this port draws (core's <c>COMPOSED_VERSION</c>).</summary>
    public const string Version = "1";

    /// <summary>The components a person answers with: each writes to the data model at its <c>value</c>.</summary>
    public static readonly IReadOnlyList<string> InputComponents = ["TextField", "Checkbox", "Select"];

    private static readonly string[] Tones = ["neutral", "info", "success", "warning", "danger"];

    private static OuroComponentSpec Spec(string description, params (string Name, OuroComposedProp Prop)[] props) =>
        new(description, props.ToDictionary(p => p.Name, p => p.Prop));

    private static OuroComposedProp Prop(string description, OuroPropKind kind, bool required = false) => new(description, kind, required);

    private static OuroComposedProp Choice(string description, string[] values, string fallback) => new(description, OuroPropKind.Enum, false, values, fallback);

    /// <summary>Ouro HX's catalog components, named as on web (core's <c>COMPONENTS</c>).</summary>
    public static readonly IReadOnlyDictionary<string, OuroComponentSpec> Components = new Dictionary<string, OuroComponentSpec>
    {
        ["Card"] = Spec("A raised surface that groups one thing's content; its children stack vertically.",
            ("children", Prop("What the card holds, top to bottom.", OuroPropKind.Children, true))),
        ["Stack"] = Spec("Lays children out in a column or a row with HX spacing.",
            ("children", Prop("What the stack holds, in order.", OuroPropKind.Children, true)),
            ("direction", Choice("vertical stacks top to bottom; horizontal lays out a row that wraps.", ["vertical", "horizontal"], "vertical")),
            ("gap", Choice("Space between children, from HX's spacing scale.", ["none", "small", "medium", "large"], "medium"))),
        ["Heading"] = Spec("A title. Level 1 is a card's or page's title, 2 a section, 3 a subsection.",
            ("text", Prop("The heading's words.", OuroPropKind.String, true)),
            ("level", new OuroComposedProp("1, 2 or 3.", OuroPropKind.Level, false, null, 2))),
        ["Text"] = Spec("A paragraph of plain text.",
            ("text", Prop("The words, plain text without Markdown.", OuroPropKind.String, true)),
            ("tone", Choice("muted for secondary text such as a byline or a note.", ["default", "muted"], "default"))),
        ["Image"] = Spec("A picture with a description for people who can't see it.",
            ("url", Prop("The image's https URL.", OuroPropKind.String, true)),
            ("description", Prop("What the picture shows, for a screen reader; never \"image of\".", OuroPropKind.String, true)),
            ("aspect", Choice("The frame's shape; the picture fills it.", ["landscape", "square", "portrait", "wide"], "landscape"))),
        ["Badge"] = Spec("A short label for a status or a category, such as \"Vegetarian\".",
            ("text", Prop("One to three words.", OuroPropKind.String, true)),
            ("tone", Choice("neutral unless the label is a status.", Tones, "neutral"))),
        ["List"] = Spec("A list of short plain-text items, such as ingredients or steps.",
            ("items", Prop("The items, in order.", OuroPropKind.Strings, true)),
            ("ordered", new OuroComposedProp("true numbers the items, for steps that happen in order.", OuroPropKind.Boolean, false, null, false))),
        ["DescriptionList"] = Spec("Pairs of a term and its detail, such as \"Time\" and \"25 min\".",
            ("items", Prop("The pairs, each { term, detail }.", OuroPropKind.Terms, true))),
        ["Separator"] = Spec("A hairline between groups of content."),
        ["Alert"] = Spec("A message the person should notice: a warning, an error, a success or information.",
            ("title", Prop("One line saying what happened.", OuroPropKind.String, true)),
            ("text", Prop("What to do about it, if anything.", OuroPropKind.String)),
            ("tone", Choice("How serious it is.", ["info", "success", "warning", "danger"], "info"))),
        ["TextField"] = Spec("A field the person types an answer into. It starts with what the data model holds at value and writes their answer there; bind an ActionButton's input to the same path, and pressing it sends what they typed.",
            ("label", Prop("What the field asks for, in a few words, such as \"Servings\".", OuroPropKind.String, true)),
            ("value", Prop("Where the answer goes, as a JSON pointer into the data model, such as /servings.", OuroPropKind.Path, true)),
            ("hint", Prop("One line under the field saying what to enter, when the label isn't enough.", OuroPropKind.String)),
            ("type", Choice("text for words; number when the answer is a number, which the field writes as one.", ["text", "number"], "text")),
            ("multiline", new OuroComposedProp("true for a longer answer over several lines, such as a note.", OuroPropKind.Boolean, false, null, false))),
        ["Checkbox"] = Spec("A yes-or-no choice the person ticks. It starts with what the data model holds at value and writes true or false there.",
            ("label", Prop("The choice, said so that ticking it means yes, such as \"Make it vegetarian\".", OuroPropKind.String, true)),
            ("value", Prop("Where the answer goes, as a JSON pointer into the data model, such as /vegetarian.", OuroPropKind.Path, true)),
            ("hint", Prop("One line under the choice, when the label isn't enough.", OuroPropKind.String))),
        ["Select"] = Spec("One choice from a short list. It starts with what the data model holds at value and writes the chosen option there, as text.",
            ("label", Prop("What the person is choosing, in a few words, such as \"Oven\".", OuroPropKind.String, true)),
            ("value", Prop("Where the choice goes, as a JSON pointer into the data model, such as /oven.", OuroPropKind.Path, true)),
            ("options", Prop("The choices, in order, as the person reads them.", OuroPropKind.Strings, true)),
            ("hint", Prop("One line under the list, when the label isn't enough.", OuroPropKind.String))),
    };

    /// <summary>A button for one of the app's declared actions (core's <c>ACTION_BUTTON</c>).</summary>
    public static readonly OuroComponentSpec ActionButton = Spec("A button for one of the app's declared actions. Its label and what pressing it does come from the action, so you choose only the action, the input you prepared and its emphasis.",
        ("action", Prop("The action's id.", OuroPropKind.Action, true)),
        ("input", Prop("The input you prepared, by field name; a value may bind to the data model with {\"@path\"}, and one bound to a field the person fills in is read when they press. Never give input to a person-only action.", OuroPropKind.Input)),
        ("emphasis", Choice("primary for the one main action on a surface; secondary for the rest.", ["primary", "secondary"], "secondary")));

    private static readonly string[] ButtonProps = ["action", "label", "consent", "press", "input", "confirm", "handoff", "emphasis", "@label"];

    /// <summary>A consent class's name in the tree: read, write, consequential or person-only.</summary>
    public static string ConsentName(OuroConsent consent) => consent switch
    {
        OuroConsent.Read => "read",
        OuroConsent.Write => "write",
        OuroConsent.Consequential => "consequential",
        _ => "person-only",
    };

    /// <summary>The consent class a name in the tree means, or null.</summary>
    public static OuroConsent? ConsentOf(string name) => name switch
    {
        "read" => OuroConsent.Read,
        "write" => OuroConsent.Write,
        "consequential" => OuroConsent.Consequential,
        "person-only" => OuroConsent.PersonOnly,
        _ => null,
    };

    /// <summary>A press's name in the tree: run, confirm or handoff.</summary>
    public static string PressName(OuroPress press) => press switch { OuroPress.Confirm => "confirm", OuroPress.Handoff => "handoff", _ => "run" };

    /// <summary>The press a name in the tree means, or null.</summary>
    public static OuroPress? PressOf(string name) => name switch { "run" => OuroPress.Run, "confirm" => OuroPress.Confirm, "handoff" => OuroPress.Handoff, _ => null };

    /// <summary>What pressing an action's button does for its consent class (core's <c>pressOf</c>).</summary>
    public static OuroPress PressOf(OuroConsent consent) => consent switch { OuroConsent.PersonOnly => OuroPress.Handoff, OuroConsent.Consequential => OuroPress.Confirm, _ => OuroPress.Run };

    /// <summary>A value bound to the data model: <c>{ "@path": "/servings" }</c>, and nothing else.</summary>
    public static bool IsBinding(JsonNode? value) => value is JsonObject o && o.Count == 1 && OuroComposedCatalog.IsString(o["@path"], out _);

    private static string BindingPath(JsonNode? value) => value!["@path"]!.GetValue<string>();

    /// <summary>A value as text: numbers and booleans as JavaScript writes them, null and missing as "", others as JSON.</summary>
    public static string AsText(JsonNode? value) => value switch
    {
        null => "",
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "",
            JsonValueKind.Number => Number(ToDouble(v)),
            _ => OuroJson.Write(v),
        },
        _ => OuroJson.Write(value),
    };

    private static double ToDouble(JsonValue v) => v.TryGetValue<double>(out var d) ? d : double.Parse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);

    // ECMAScript's Number::toString: the shortest digits that read back as the number, written plainly from 1e-6 up to
    // 1e21 and with an exponent outside that, so a confirm sheet shows a long id exactly as the press sends it.
    private static string Number(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsInfinity(d)) return d > 0 ? "Infinity" : "-Infinity";
        if (d == 0) return "0";
        var r = Math.Abs(d).ToString("R", CultureInfo.InvariantCulture);
        var e = r.IndexOf('E');
        var exponent = e < 0 ? 0 : int.Parse(r[(e + 1)..], CultureInfo.InvariantCulture);
        var mantissa = e < 0 ? r : r[..e];
        var dot = mantissa.IndexOf('.');
        var whole = dot < 0 ? mantissa : mantissa[..dot];
        var all = whole + (dot < 0 ? "" : mantissa[(dot + 1)..]);
        var digits = all.TrimStart('0');
        var n = whole.Length - (all.Length - digits.Length) + exponent;
        digits = digits.TrimEnd('0');
        var k = digits.Length;
        var sign = d < 0 ? "-" : "";
        if (k <= n && n <= 21) return sign + digits + new string('0', n - k);
        if (0 < n && n <= 21) return $"{sign}{digits[..n]}.{digits[n..]}";
        if (-6 < n && n <= 0) return $"{sign}0.{new string('0', -n)}{digits}";
        var x = n - 1;
        return $"{sign}{digits[0]}{(k > 1 ? "." + digits[1..] : "")}e{(x < 0 ? "-" : "+")}{Math.Abs(x)}";
    }

    /// <summary>
    /// The props of an ActionButton for an action and the input an agent prepared; everything but the input comes from
    /// the catalog (core's <c>actionButtonProps</c>). <paramref name="label"/> is the app's own name for an id a confirm
    /// sheet shows, such as a recipe's title; it is asked only for an id field (one whose catalog input has <c>shownAs</c>)
    /// and wins over what the agent prepared. Every other row shows exactly what will be sent.
    /// </summary>
    public static JsonObject ActionButtonProps(OuroComposedCatalog catalog, string action, JsonObject prepared, Func<OuroLabelContext, string?>? label = null)
    {
        var a = catalog.Actions[action];
        var input = a.PersonOnly is not null ? new JsonObject() : (JsonObject)prepared.DeepClone();
        var props = new JsonObject { ["action"] = action, ["label"] = a.Label, ["consent"] = ConsentName(a.Consent), ["press"] = PressName(a.Press), ["input"] = input };
        if (a.Press == OuroPress.Confirm) props["confirm"] = new JsonObject { ["title"] = a.Label, ["description"] = a.Description, ["fields"] = ConfirmFields(action, a, input, label) };
        if (a.PersonOnly is { } po) props["handoff"] = po.How;
        return props;
    }

    // The input fields a confirm sheet has a row for, in the catalog's order: each prepared one, less a name that stands
    // in for an id.
    private static List<string> ConfirmKeys(OuroCatalogAction a, JsonObject input)
    {
        var names = a.Input.Where(f => input.ContainsKey(f.Key) && f.Value.ShownAs is { } s && a.Input.ContainsKey(s)).Select(f => f.Value.ShownAs!).ToHashSet();
        return a.Input.Keys.Where(k => input.ContainsKey(k) && !names.Contains(k)).ToList();
    }

    // The rows of a confirm sheet: each prepared input with the person's label. Only an id field (one with shownAs) shows
    // a name (the app's resolver, else the sibling shownAs names), with the id as raw beside it; every other row shows
    // exactly what will be sent. An input bound to a field shows what the person wrote, at the press.
    private static JsonArray ConfirmFields(string action, OuroCatalogAction a, JsonObject input, Func<OuroLabelContext, string?>? label)
    {
        var rows = new JsonArray();
        foreach (var k in ConfirmKeys(a, input))
        {
            var f = a.Input[k];
            if (IsBinding(input[k]))
            {
                rows.Add(new JsonObject { ["label"] = f.Label, ["value"] = "", ["bind"] = BindingPath(input[k]) });
                continue;
            }
            var raw = AsText(input[k]);
            var sibling = f.ShownAs is { } shownAs ? input[shownAs] : null;
            var prepared = OuroComposedCatalog.IsString(sibling, out var name) && name.Trim().Length > 0 ? name.Trim() : null;
            var value = (f.ShownAs is not null ? label?.Invoke(new OuroLabelContext(action, k, raw, input)) ?? prepared : null) ?? raw;
            rows.Add(value == raw ? new JsonObject { ["label"] = f.Label, ["value"] = value } : new JsonObject { ["label"] = f.Label, ["value"] = value, ["raw"] = raw });
        }
        return rows;
    }

    /// <summary>
    /// Every problem in a composed tree against a catalog (core's <c>validateTree</c>): each component is the catalog's,
    /// each prop is resolved and of its kind, and each ActionButton is exactly what the catalog says its action's button
    /// is, so no sender can relabel a button or loosen its consent. Empty means it can be drawn.
    /// </summary>
    public static IReadOnlyList<OuroTreeProblem> ValidateTree(OuroComposedCatalog catalog, JsonNode? tree, string path = "")
    {
        if (Twice(tree, path) is { } twice) return [twice];
        var problems = new List<OuroTreeProblem>();
        Visit(catalog, tree, path, problems);
        return problems;
    }

    // System.Text.Json parses an object that names a key twice, then throws when it is first read. JSON.parse keeps the
    // last; a tree that says one thing twice is refused here instead, before anything reads it.
    internal static OuroTreeProblem? Twice(JsonNode? node, string at)
    {
        switch (node)
        {
            case JsonObject o:
                try { _ = o.Count; }
                catch (ArgumentException) { return new(at, "names a key twice. Fix: give each key once."); }
                foreach (var (k, v) in o) if (Twice(v, $"{at}/{k.Replace("~", "~0").Replace("/", "~1")}") is { } p) return p;
                return null;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++) if (Twice(a[i], $"{at}/{i}") is { } p) return p;
                return null;
            default:
                return null;
        }
    }

    private static void Visit(OuroComposedCatalog cat, JsonNode? n, string at, List<OuroTreeProblem> problems)
    {
        if (n is not JsonObject node || !OuroComposedCatalog.IsString(node["id"], out _) || !OuroComposedCatalog.IsString(node["component"], out var component) || node["props"] is not JsonObject props || node["children"] is not JsonArray children)
        {
            problems.Add(new(at, "A node is { id, component, props, children }."));
            return;
        }
        if (!cat.Components.TryGetValue(component, out var spec))
        {
            problems.Add(new($"{at}/component", $"{Stringify(JsonValue.Create(component))} is not in this catalog; use one of {string.Join(", ", cat.Components.Keys)}."));
            return;
        }
        var allowed = component == "ActionButton" ? ButtonProps : spec.Props.Where(p => p.Value.Kind != OuroPropKind.Children).Select(p => p.Key).Append("@label").ToArray();
        foreach (var (key, _) in props) if (!allowed.Contains(key)) problems.Add(new($"{at}/props/{key}", $"{component} has no {key}."));
        if (props.TryGetPropertyValue("@label", out var lbl) && !OuroComposedCatalog.IsString(lbl, out _)) problems.Add(new($"{at}/props/@label", "@label is text."));
        foreach (var (key, p) in spec.Props)
        {
            if (p.Kind is OuroPropKind.Children or OuroPropKind.Action or OuroPropKind.Input) continue;
            if (!props.TryGetPropertyValue(key, out var v))
            {
                if (p.Required) problems.Add(new($"{at}/props/{key}", $"{component} needs {key}: {p.Description}"));
                continue;
            }
            CheckProp(problems, key, p, v, $"{at}/props/{key}");
        }
        if (component == "ActionButton") CheckButton(cat, props, at, problems);
        if (!spec.Props.ContainsKey("children") && children.Count > 0) problems.Add(new($"{at}/children", $"{component} has no children."));
        for (var i = 0; i < children.Count; i++) Visit(cat, children[i], $"{at}/children/{i}", problems);
    }

    private static void CheckButton(OuroComposedCatalog cat, JsonObject props, string at, List<OuroTreeProblem> problems)
    {
        var hasId = props.TryGetPropertyValue("action", out var idNode);
        if (!OuroComposedCatalog.IsString(idNode, out var id) || !cat.Actions.TryGetValue(id, out var a))
        {
            problems.Add(new($"{at}/props/action", $"{(hasId ? Stringify(idNode) : "undefined")} is not one of the app's actions: {string.Join(", ", cat.Actions.Keys)}."));
            return;
        }
        if (props["input"] is not JsonObject input)
        {
            problems.Add(new($"{at}/props/input", "input is an object of field values."));
            return;
        }
        if (a.PersonOnly is { } po && input.Count > 0)
        {
            problems.Add(new($"{at}/props/input", po.Refusal["summary"]?.GetValue<string>() ?? "", (JsonObject)po.Refusal.DeepClone()));
            return;
        }
        foreach (var (k, v) in input)
        {
            // A field the catalog doesn't carry would be sent without a row on the confirm sheet.
            if (!a.Input.ContainsKey(k))
                problems.Add(new($"{at}/props/input/{k}", $"{id} has no input {k}; its fields are {(a.Input.Count > 0 ? string.Join(", ", a.Input.Keys) : "none")}."));
            if (v is JsonObject o && o.ContainsKey("@path") && !(IsBinding(v) && OuroPointer.IsData(BindingPath(v))))
                problems.Add(new($"{at}/props/input/{k}", $"{k} is a value, or {{\"@path\": \"/...\"}} bound to the data model."));
        }
        var expected = ActionButtonProps(cat, id, input);
        foreach (var key in new[] { "label", "consent", "press", "handoff" })
        {
            var has = props.TryGetPropertyValue(key, out var given);
            var wants = expected.TryGetPropertyValue(key, out var want);
            if (has != wants || (has && !JsonNode.DeepEquals(given, want)))
                problems.Add(new($"{at}/props/{key}", $"{id}'s {key} is the catalog's {Stringify(want)}, never the sender's."));
        }
        if (a.Press == OuroPress.Confirm)
        {
            if (props["confirm"] is not JsonObject c || !OuroComposedCatalog.IsString(c["title"], out var title) || title != a.Label || !OuroComposedCatalog.IsString(c["description"], out var description) || description != a.Description || c["fields"] is not JsonArray rows)
                problems.Add(new($"{at}/props/confirm", $"{id} confirms, with the catalog's title and description and a row per prepared input."));
            else
                ConfirmRowProblems(problems, id, a, input, rows, $"{at}/props/confirm/fields");
        }
        else if (props.ContainsKey("confirm"))
        {
            problems.Add(new($"{at}/props/confirm", $"{id} doesn't confirm; only a consequential action does."));
        }
    }

    // A confirm sheet's rows against the rows the catalog gives for the prepared input (core's confirmRowProblems): the
    // same fields, in order, with the catalog's labels, and each row showing what will be sent (its raw, or its value when
    // it has none) or, for a bound input, the same binding. Only the words shown for an id may differ, since the app's own
    // resolver names it; any other row shows exactly the value sent, with no raw to hide it in.
    private static void ConfirmRowProblems(List<OuroTreeProblem> problems, string id, OuroCatalogAction a, JsonObject input, JsonArray rows, string at)
    {
        var expected = ConfirmFields(id, a, input, null);
        var keys = ConfirmKeys(a, input);
        void Bad() => problems.Add(new(at, $"{id}'s confirm sheet shows a row for each prepared input, with its label and the value that will be sent; the catalog's rows are {OuroJson.Write(expected)}."));
        if (rows.Count != expected.Count)
        {
            Bad();
            return;
        }
        for (var i = 0; i < rows.Count; i++)
        {
            var want = (JsonObject)expected[i]!;
            var wantValue = want["value"]!.GetValue<string>();
            if (rows[i] is not JsonObject row || !OuroComposedCatalog.IsString(row["label"], out var label) || label != want["label"]!.GetValue<string>())
            {
                Bad();
                continue;
            }
            if (want["bind"] is JsonNode bind)
            {
                if (!OuroComposedCatalog.IsString(row["bind"], out var b) || b != bind.GetValue<string>() || row.ContainsKey("raw")) Bad();
            }
            else if (row.ContainsKey("bind") || !OuroComposedCatalog.IsString(row["value"], out var value)) Bad();
            else if (a.Input[keys[i]].ShownAs is not null)
            {
                // JavaScript's row.raw ?? row.value: a raw of null stands aside for the value.
                var shown = row["raw"] is null ? row["value"] : row["raw"];
                var sent = want["raw"]?.GetValue<string>() ?? wantValue;
                if (!OuroComposedCatalog.IsString(shown, out var s) || s != sent) Bad();
            }
            else if (value != wantValue || row.ContainsKey("raw")) Bad();
        }
    }

    private static void CheckProp(List<OuroTreeProblem> problems, string key, OuroComposedProp p, JsonNode? v, string at)
    {
        string? expected = p.Kind switch
        {
            OuroPropKind.String when !OuroComposedCatalog.IsString(v, out _) => "text",
            OuroPropKind.Strings when v is not JsonArray list || !list.All(s => OuroComposedCatalog.IsString(s, out _)) => "a list of text",
            OuroPropKind.Boolean when v is not JsonValue b || b.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False) => "true or false",
            OuroPropKind.Enum when !OuroComposedCatalog.IsString(v, out var e) || !p.Values!.Contains(e) => $"one of {string.Join(", ", p.Values!)}",
            OuroPropKind.Level when v is not JsonValue n || n.GetValueKind() != JsonValueKind.Number || ToDouble(n) is not (1d or 2d or 3d) => "1, 2 or 3",
            OuroPropKind.Path when !OuroComposedCatalog.IsString(v, out var path) || !OuroPointer.IsData(path) => "a JSON pointer into the data model, such as /servings",
            OuroPropKind.Terms when v is not JsonArray terms || !terms.All(t => t is JsonObject o && o.Count == 2 && OuroComposedCatalog.IsString(o["term"], out _) && OuroComposedCatalog.IsString(o["detail"], out _)) => "a list of { term, detail } text",
            _ => null,
        };
        if (expected is not null) problems.Add(new(at, $"{key} is {expected}, got {Stringify(v)}."));
    }

    private static string Stringify(JsonNode? v) => v is null ? "null" : OuroJson.Write(v);

    /// <summary>
    /// A button as it stands when the person presses it (core's <c>resolveButton</c>): each input bound to the data model
    /// replaced by what the data holds there now (null when nothing), and each bound row of its confirm sheet showing that
    /// value. Call it with the current data before showing the confirm sheet.
    /// </summary>
    public static OuroComposedNode ResolveButton(OuroComposedNode button, JsonNode? data)
    {
        if (button.Props["input"] is not JsonObject prepared || !prepared.Any(p => IsBinding(p.Value))) return button;
        var input = new JsonObject();
        foreach (var (k, v) in prepared) input[k] = IsBinding(v) ? OuroPointer.Get(data, BindingPath(v))?.DeepClone() : v?.DeepClone();
        var props = (JsonObject)button.Props.DeepClone();
        props["input"] = input;
        if (button.Props["confirm"] is JsonObject confirm && confirm["fields"] is JsonArray fields)
        {
            var sheet = (JsonObject)confirm.DeepClone();
            sheet["fields"] = new JsonArray(fields.Select(row =>
            {
                if (row is not JsonObject r || !OuroComposedCatalog.IsString(r["bind"], out var bind)) return row?.DeepClone();
                var shown = new JsonObject();
                if (r.TryGetPropertyValue("label", out var l)) shown["label"] = l?.DeepClone();
                shown["value"] = AsText(OuroPointer.Get(data, bind));
                return shown;
            }).ToArray());
            props["confirm"] = sheet;
        }
        return button with { Props = props };
    }

    /// <summary>
    /// What pressing a composed button asks the app to do (core's <c>personPress</c>), with the input as the agent
    /// prepared it. A person-only button never asks: it hands the person to the app's own screen. A consequential one asks
    /// only once <paramref name="confirmed"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The button hands off, or confirms and the person hasn't.</exception>
    public static OuroPersonPress PersonPress(OuroComposedNode button, bool confirmed = false) => Press(button.Props, confirmed);

    /// <summary>The same press, with each input bound to a field read from <paramref name="data"/> as the person left it.</summary>
    public static OuroPersonPress PersonPress(OuroComposedNode button, bool confirmed, JsonNode? data) => Press(ResolveButton(button, data).Props, confirmed);

    private static OuroPersonPress Press(JsonObject p, bool confirmed)
    {
        var action = AsText(p["action"]);
        var press = AsText(p["press"]);
        if (press == "handoff") throw new InvalidOperationException($"{action} is person-only; its button hands the person to the app, so it sends no action.");
        if (press == "confirm" && !confirmed) throw new InvalidOperationException($"{action} is consequential; send its action only after the person confirms.");
        var consent = ConsentOf(AsText(p["consent"])) ?? throw new InvalidOperationException($"{action}: its consent \"{AsText(p["consent"])}\" isn't one of read, write, consequential or person-only. Fix: validate the tree first.");
        return new OuroPersonPress(action, p["input"]?.DeepClone() as JsonObject ?? new JsonObject(), consent, AsText(p["label"]), press == "confirm");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Updating a tree a renderer already draws (core's applyTreeUpdate, decision 2026-11-18-composed-tree-updates.md)
    // -----------------------------------------------------------------------------------------------------------------

    private const string UpdateShape = "An update is { nodes?, data? }: nodes replace the tree's nodes by id, and data sets values by JSON pointer.";

    /// <summary>
    /// Every problem in an update that needs no tree (core's <c>treeUpdateProblems</c>): its shape, each node against the
    /// catalog (at <c>/nodes/&lt;i&gt;</c>) and each data path, which is absolute and never names <c>__proto__</c>,
    /// <c>constructor</c> or <c>prototype</c>. <see cref="ApplyTreeUpdate"/> checks it too, then the ids and the tree it gives.
    /// </summary>
    public static IReadOnlyList<OuroTreeProblem> TreeUpdateProblems(OuroComposedCatalog catalog, JsonNode? update)
    {
        if (update is not JsonObject u) return [new("", UpdateShape)];
        if (Twice(u, "") is { } twice) return [twice];
        if (NonJsonAt(u, "") is { } notJson) return [new(notJson, "An update is JSON: text, finite numbers, true, false, null, lists and objects.")];
        var problems = new List<OuroTreeProblem>();
        foreach (var (key, _) in u) if (key is not ("nodes" or "data")) problems.Add(new($"/{key}", $"An update has no {key}. {UpdateShape}"));
        if (u.TryGetPropertyValue("nodes", out var nodes))
        {
            if (nodes is not JsonArray list) problems.Add(new("/nodes", "nodes is a list of nodes, each replacing the tree's node with its id."));
            else for (var i = 0; i < list.Count; i++) problems.AddRange(ValidateTree(catalog, list[i], $"/nodes/{i}"));
        }
        if (u.TryGetPropertyValue("data", out var data))
        {
            if (data is not JsonArray entries) problems.Add(new("/data", "data is a list of { path, value }, each setting the data model at a JSON pointer."));
            else
                for (var i = 0; i < entries.Count; i++)
                {
                    if (entries[i] is not JsonObject d || !d.ContainsKey("value") || d.Any(e => e.Key is not ("path" or "value")))
                        problems.Add(new($"/data/{i}", "A data entry is { path, value }: value is JSON, and null deletes."));
                    else if (!OuroComposedCatalog.IsString(d["path"], out var path) || !OuroPointer.IsData(path))
                        problems.Add(new($"/data/{i}/path", "path is a JSON pointer into the data model, such as /servings, and never names __proto__, constructor or prototype."));
                }
        }
        return problems;
    }

    // Where a value first stops being JSON: parsed JSON never does, but a value built in code can hold NaN or infinity.
    private static string? NonJsonAt(JsonNode? v, string at)
    {
        switch (v)
        {
            case JsonObject o:
                foreach (var (k, x) in o) if (NonJsonAt(x, $"{at}/{k.Replace("~", "~0").Replace("/", "~1")}") is { } found) return found;
                return null;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++) if (NonJsonAt(a[i], $"{at}/{i}") is { } found) return found;
                return null;
            case JsonValue n when n.GetValueKind() == JsonValueKind.Number:
                return double.IsFinite(ToDouble(n)) ? null : at;
            default:
                return null;
        }
    }

    // Why setting the pointer in the data would not give JSON: a list is written only at an index from 0 to its length.
    private static string? ListIndexProblem(JsonNode? data, string pointer)
    {
        var cur = data;
        foreach (var k in (pointer.StartsWith('/') ? pointer[1..] : pointer).Split('/').Select(k => k.Replace("~1", "/").Replace("~0", "~")))
        {
            if (cur is JsonArray list)
            {
                if (k.Length == 0 || !k.All(char.IsAsciiDigit) || k.Length > 9 || int.Parse(k, CultureInfo.InvariantCulture) > list.Count)
                    return $"{pointer} names {Stringify(JsonValue.Create(k))} in a list of {list.Count}; in a list, a segment is an index from 0 to {list.Count}.";
                var i = int.Parse(k, CultureInfo.InvariantCulture);
                cur = i < list.Count ? list[i] : null;
            }
            else if (cur is JsonObject o) cur = o.TryGetPropertyValue(k, out var next) ? next : null;
            else return null;
        }
        return null;
    }

    private static int CountId(JsonNode node, string id) =>
        (node["id"] is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() == id ? 1 : 0) + (node["children"] as JsonArray ?? []).Sum(c => c is null ? 0 : CountId(c, id));

    private static JsonNode ReplaceNode(JsonNode node, JsonNode by, string id)
    {
        if (node["id"] is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() == id) return by.DeepClone();
        var copy = node.DeepClone();
        if (copy["children"] is JsonArray children)
            for (var i = 0; i < children.Count; i++)
                if (children[i] is { } c) children[i] = ReplaceNode(c, by, id);
        return copy;
    }

    /// <summary>
    /// A tree and its data model after an update (core's <c>applyTreeUpdate</c>), or every problem and no change: never a
    /// tree partly updated. Each node replaces the one node of the tree with its id, children and all; an id the tree
    /// lacks is a problem, and so is one a template's instances share. Each data entry sets a value at its JSON pointer,
    /// null deleting; <c>/</c> is the whole data model, which stays an object, and a list is written only at an index from
    /// 0 to its length. The result passes <see cref="ValidateTree"/>, so an update can't relabel a button or loosen its
    /// consent any more than a whole tree can. It changes neither input, and shares no node with them.
    /// </summary>
    public static OuroTreeUpdateResult ApplyTreeUpdate(OuroComposedCatalog catalog, JsonNode? tree, JsonNode? data, JsonNode? update)
    {
        var shape = TreeUpdateProblems(catalog, update);
        if (shape.Count > 0) return new(null, null, shape);
        var given = ValidateTree(catalog, tree);
        if (given.Count > 0) return new(null, null, given);
        var problems = new List<OuroTreeProblem>();
        var next = tree!.DeepClone();
        if (update!["nodes"] is JsonArray nodes)
            for (var i = 0; i < nodes.Count; i++)
            {
                var id = nodes[i]!["id"]!.GetValue<string>();
                var found = CountId(next, id);
                if (found == 0) problems.Add(new($"/nodes/{i}/id", $"{Stringify(JsonValue.Create(id))} is not in the tree; replace a node it has, or the node that should hold this one."));
                else if (found > 1) problems.Add(new($"/nodes/{i}/id", $"{Stringify(JsonValue.Create(id))} names {found} nodes, a template's instances; replace the node that holds them, by its own id."));
                else next = ReplaceNode(next, nodes[i]!, id);
            }
        var model = data?.DeepClone();
        if (update["data"] is JsonArray entries)
            for (var i = 0; i < entries.Count; i++)
            {
                var path = entries[i]!["path"]!.GetValue<string>();
                var baseline = model ?? new JsonObject();
                if (ListIndexProblem(baseline, path) is { } index)
                {
                    problems.Add(new($"/data/{i}/path", index));
                    continue;
                }
                var written = OuroPointer.Set(baseline, path, entries[i]!["value"]);
                if (written is not JsonObject)
                {
                    problems.Add(new($"/data/{i}/value", "The data model is an object; set / only to an object."));
                    continue;
                }
                model = written;
            }
        if (problems.Count > 0) return new(null, null, problems);
        var invalid = ValidateTree(catalog, next);
        return invalid.Count > 0 ? new(null, null, invalid) : new(next, model, []);
    }

    /// <summary>
    /// The fields' data model when the agent sends new data (web's <c>ComposedSurface</c>): the new data, with each answer
    /// the person entered kept where the agent left its value as it was, and the list item it is in. Where the agent
    /// changed either, the agent's value wins and the person's answer is dropped. <paramref name="edited"/> holds the
    /// pointers the person wrote; the answers kept are returned with the data.
    /// </summary>
    public static (JsonObject Data, IReadOnlySet<string> Edited) KeepAnswers(JsonNode? before, JsonNode? after, JsonNode? current, IEnumerable<string> edited)
    {
        var was = before ?? new JsonObject();
        var now = after?.DeepClone() as JsonObject ?? new JsonObject();
        // Compared as JSON text, as web compares them: an object with its keys reordered counts as changed.
        bool Same(string p) => Text(was, p) == Text(now, p);
        var kept = edited.Where(p => Anchors(was, p).All(Same)).ToHashSet();
        foreach (var p in kept) if (OuroPointer.Set(now, p, OuroPointer.Get(current, p)?.DeepClone()) is JsonObject o) now = o;
        return (now, kept);
    }

    private static string? Text(JsonNode data, string pointer) => OuroPointer.TryGet(data, pointer, out var v) ? (v is null ? "null" : v.ToJsonString()) : null;

    // The pointers an answer at a pointer depends on: the pointer itself and each list item on the way to it, since a list
    // that loses an item moves the next one into its place. The data model is an object, so the first key is never one.
    private static IEnumerable<string> Anchors(JsonNode data, string pointer)
    {
        var keys = pointer.Split('/').Skip(1).ToArray();
        var prefixes = keys.Select((_, i) => "/" + string.Join("/", keys.Take(i + 1))).ToArray();
        return prefixes.Where((_, i) => i == keys.Length - 1 || (i > 0 && OuroPointer.Get(data, prefixes[i - 1]) is JsonArray));
    }
}

/// <summary>What <see cref="OuroComposed.ApplyTreeUpdate"/> gives: the new tree and data model, or only the problems.</summary>
public sealed record OuroTreeUpdateResult(JsonNode? Tree, JsonNode? Data, IReadOnlyList<OuroTreeProblem> Problems)
{
    /// <summary>Whether the update applied: no problems, and a tree.</summary>
    public bool Applied => Problems.Count == 0;
}
