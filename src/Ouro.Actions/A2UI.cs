// The A2UI v1.0 adapter, ported from ouro-ui-core's a2ui.mts (vendor/core/ouro-a2ui.mts): it validates a stream of A2UI
// messages against an app's catalog, resolves it into Ouro's composed tree and data model, and wraps a person's press in
// A2UI's action message. Everything that isn't A2UI's (the components, the consent rules, the tree and its validator) is
// Composed.cs's, so the tree an A2UI stream gives is drawn and checked exactly as one an agent sends itself. Problems
// come in A2UI's validation error format, word for word as core writes them, so an agent corrects itself the same way on
// every kit. Decisions: core docs/decisions/2026-11-14-composed-ui-own-tree.md and 2026-11-17-composed-tree-leads.md.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ouro.Actions;

/// <summary>
/// A problem in a stream of A2UI messages, in A2UI's validation error format (core's <c>Problem</c>): its code, the surface
/// or function call it is about, a JSON pointer into the stream, one sentence, and the typed refusal when one applies.
/// </summary>
public sealed record OuroA2UIProblem(string Code, string Path, string Message, string? SurfaceId = null, string? FunctionCallId = null, JsonObject? Refusal = null)
{
    /// <summary>The problem as JSON, as core writes it: hand it to the agent to correct the stream.</summary>
    public JsonObject ToJson()
    {
        var o = new JsonObject { ["code"] = Code };
        if (SurfaceId is not null) o["surfaceId"] = SurfaceId;
        if (FunctionCallId is not null) o["functionCallId"] = FunctionCallId;
        o["path"] = Path;
        o["message"] = Message;
        if (Refusal is not null) o["refusal"] = Refusal.DeepClone();
        return o;
    }

    /// <summary>The same problem as a composed tree's (<see cref="OuroTreeProblem"/>): its path, message and refusal.</summary>
    public OuroTreeProblem ToTreeProblem() => new(Path, Message, Refusal?.DeepClone() as JsonObject);

    public override string ToString() => $"{Path} {Message}";
}

/// <summary>
/// One surface an A2UI stream leaves: its id, its composed tree (null when it has no component with id <c>root</c>, so
/// it shows nothing) and its data model, which the tree's fields start from.
/// </summary>
public sealed record OuroA2UISurface(string SurfaceId, JsonObject? Tree, JsonNode? Data);

/// <summary>What <see cref="OuroA2UI.ResolveMessages"/> gives: every surface the stream leaves, in order, or only the problems.</summary>
public sealed record OuroA2UIResolved(IReadOnlyList<OuroA2UISurface> Surfaces, IReadOnlyList<OuroA2UIProblem> Problems)
{
    /// <summary>Whether the stream passed the checks, so its surfaces can be drawn.</summary>
    public bool Valid => Problems.Count == 0;

    /// <summary>As core's <c>resolveMessages</c> writes it: <c>{ surfaces: { id: tree or null } }</c>, or <c>{ problems }</c>.</summary>
    public JsonObject ToJson()
    {
        if (!Valid) return new JsonObject { ["problems"] = new JsonArray(Problems.Select(p => (JsonNode)p.ToJson()).ToArray()) };
        var surfaces = new JsonObject();
        foreach (var s in Surfaces) surfaces[s.SurfaceId] = s.Tree?.DeepClone();
        return new JsonObject { ["surfaces"] = surfaces };
    }
}

/// <summary>
/// A2UI v1.0 onto Ouro's composed tree (core's <c>a2ui.mts</c>): validate a message stream against the app's catalog,
/// resolve it into the tree <see cref="OuroComposed.ValidateTree"/> checks and a surface draws, and send a press as A2UI's
/// <c>action</c> message.
/// </summary>
public static class OuroA2UI
{
    /// <summary>The A2UI protocol version this adapter reads (core's <c>A2UI_VERSION</c>).</summary>
    public const string Version = "1.0";

    /// <summary>Ouro HX's catalog id; an app's catalog, with its actions, takes its own (core's <c>HX_CATALOG_ID</c>).</summary>
    public const string HxCatalogId = "https://ui.ouroboros.bot/a2ui/v1/catalog.json";

    /// <summary>The catalog-scope extension key that carries what each action's button does.</summary>
    public const string Extension = "bot_ouroboros_hx";

    /// <summary>The action-egress extension key on every A2UI <c>action</c> a composed button sends.</summary>
    public const string ActionExtension = "bot_ouroboros_action";

    private const string Failed = "VALIDATION_FAILED", FunctionCall = "INVALID_FUNCTION_CALL", Integrity = "INTEGRITY_ERROR";

    /// <summary>
    /// How many components deep a surface's tree may go. A stream names children by id, so no JSON nesting limit bounds
    /// its depth; past this the stream is refused, so no stream can exhaust the stack. A tree an agent sends itself is
    /// bounded by its JSON's depth, about as deep.
    /// </summary>
    public const int MaxDepth = 24;

    /// <summary>
    /// How many nodes one surface may resolve to. Shared children and templates over lists multiply; past this the stream
    /// is refused, so no stream can exhaust memory or hang the screen that draws it.
    /// </summary>
    public const int MaxNodes = 5000;

    /// <summary>
    /// How much one surface may copy out of its data model and its messages: each value its tree shows or sends costs its
    /// text's length plus one, and a list or object one more than what it holds. A value bound to the data model is copied
    /// into every node that shows it, so a template over a long list multiplies it; past this the stream is refused, so no
    /// stream can exhaust memory.
    /// </summary>
    public const int MaxSize = 1_000_000;

    private static readonly string[] MessageKeys = ["createSurface", "updateComponents", "updateDataModel", "deleteSurface", "callRendererFunction", "agentFunctionResponse"];

    // -----------------------------------------------------------------------------------------------------------------
    // A tool result
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The A2UI messages a tool result carries in <c>structuredContent.a2ui.messages</c> (core's <c>mcpAppToolResult</c>);
    /// false when it carries none. A result that names a key twice gives the whole result, which
    /// <see cref="ValidateMessages"/> then refuses.
    /// </summary>
    public static bool TryReadToolResult(JsonNode? toolResult, out JsonNode? messages)
    {
        messages = null;
        if (toolResult is not JsonObject result) return false;
        if (OuroComposed.Twice(result, "") is not null)
        {
            messages = result;
            return true;
        }
        if (result["structuredContent"] is not JsonObject content || content["a2ui"] is not JsonObject a2ui || !a2ui.ContainsKey("messages")) return false;
        messages = a2ui["messages"];
        return true;
    }

    /// <summary>
    /// A tool result carrying A2UI messages (core's <c>mcpAppToolResult</c>): <c>content</c> is the text an agent and a
    /// text-only host read, and <c>structuredContent.a2ui</c> the surface a view draws.
    /// </summary>
    /// <exception cref="InvalidOperationException">The messages don't validate against the catalog; the message lists why.</exception>
    public static JsonObject McpAppToolResult(string text, OuroComposedCatalog catalog, JsonNode? messages)
    {
        var problems = ValidateMessages(catalog, messages);
        if (problems.Count > 0) throw new InvalidOperationException($"the A2UI messages don't validate: {string.Join("; ", problems)}");
        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["structuredContent"] = new JsonObject { ["a2ui"] = new JsonObject { ["version"] = Version, ["catalogId"] = catalog.CatalogId, ["messages"] = messages!.DeepClone() } },
        };
    }

    // -----------------------------------------------------------------------------------------------------------------
    // The validator
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>Why an agent can't press a button through the screen: the message for a <c>callRendererFunction</c> (core's <c>functionCallProblem</c>).</summary>
    public static (string Message, JsonObject? Refusal) FunctionCallProblem(OuroComposedCatalog catalog, JsonNode? name)
    {
        var isName = OuroComposedCatalog.IsString(name, out var n);
        var a = isName ? catalog.Actions.GetValueOrDefault(n) : null;
        if (a?.PersonOnly is { } po) return (Summary(po), (JsonObject)po.Refusal.DeepClone());
        if (a is not null) return ($"{n} is an action, and agents run actions through their own tool (MCP {n}), not through the person's screen.", null);
        return ($"This catalog has no functions an agent may call{(isName ? $", so {n} is not one" : "")}.", null);
    }

    private static string Summary(OuroCatalogPersonOnly po) => OuroComposedCatalog.IsString(po.Refusal["summary"], out var s) ? s : "";

    private sealed class Walk(OuroComposedCatalog cat, List<OuroA2UIProblem> problems, string surfaceId)
    {
        public OuroComposedCatalog Cat { get; } = cat;

        public void Add(string path, string message, JsonObject? refusal = null) => problems.Add(new(Failed, path, message, surfaceId, null, refusal));
    }

    /// <summary>
    /// Every problem in a stream of A2UI messages against a catalog (core's <c>validateMessages</c>): the envelope, each
    /// component and its props, every child id resolving, one root per surface, no cycles, no pointer through
    /// <c>__proto__</c>, <c>constructor</c> or <c>prototype</c>, and the action rules. Empty means it can be drawn.
    /// </summary>
    public static IReadOnlyList<OuroA2UIProblem> ValidateMessages(OuroComposedCatalog catalog, JsonNode? messages)
    {
        if (OuroComposed.Twice(messages, "") is { } twice) return [new(Failed, twice.Path, twice.Message)];
        if (messages is not JsonArray list) return [new(Failed, "", "Send a list of A2UI messages.")];
        var problems = new List<OuroA2UIProblem>();
        var surfaces = new OrderedDictionary<string, OrderedDictionary<string, (JsonObject Comp, string Path)>>();
        for (var i = 0; i < list.Count; i++)
        {
            var at = $"/{i}";
            if (list[i] is not JsonObject m || !OuroComposedCatalog.IsString(m["version"], out var version) || version != Version)
            {
                problems.Add(new(Failed, $"{at}/version", $"Every message has \"version\": \"{Version}\"."));
                continue;
            }
            var keys = m.Select(p => p.Key).Where(k => k != "version").ToArray();
            if (keys.Length != 1 || !MessageKeys.Contains(keys[0]))
            {
                problems.Add(new(Failed, at, $"A message has exactly one of {string.Join(", ", MessageKeys)}."));
                continue;
            }
            var key = keys[0];
            if (m[key] is not JsonObject body)
            {
                problems.Add(new(Failed, $"{at}/{key}", $"{key} is an object."));
                continue;
            }
            if (key == "callRendererFunction")
            {
                var (message, refusal) = FunctionCallProblem(catalog, body["callFunction"] is JsonObject call ? call["@call"] : null);
                problems.Add(new(FunctionCall, $"{at}/callRendererFunction/callFunction", message, null, OuroComposedCatalog.IsString(body["functionCallId"], out var callId) ? callId : "", refusal));
                continue;
            }
            if (key == "agentFunctionResponse")
            {
                problems.Add(new(FunctionCall, $"{at}/agentFunctionResponse", "This catalog's renderers call no agent functions, so there is nothing to answer."));
                continue;
            }
            if (!OuroComposedCatalog.IsString(body["surfaceId"], out var surfaceId) || surfaceId == "")
            {
                problems.Add(new(Failed, $"{at}/{key}/surfaceId", "surfaceId is a non-empty string."));
                continue;
            }
            var w = new Walk(catalog, problems, surfaceId);
            if (key == "createSurface")
            {
                if (surfaces.ContainsKey(surfaceId)) problems.Add(new(Integrity, $"{at}/createSurface/surfaceId", $"Surface {surfaceId} already exists; delete it first or use another id.", surfaceId));
                if (!OuroComposedCatalog.IsString(body["catalogId"], out var catalogId) || catalogId != catalog.CatalogId) w.Add($"{at}/createSurface/catalogId", $"catalogId is {catalog.CatalogId}.");
                if (body.ContainsKey("theme")) w.Add($"{at}/createSurface/theme", "A2UI v1.0 has no theme; the app's HX theme styles the surface.");
                if (body.ContainsKey("dataModel") && body["dataModel"] is not JsonObject) w.Add($"{at}/createSurface/dataModel", "dataModel is an object.");
                surfaces[surfaceId] = new();
            }
            else if (!surfaces.ContainsKey(surfaceId))
            {
                w.Add($"{at}/{key}/surfaceId", $"Create surface {surfaceId} before {key}.");
                continue;
            }
            if (key == "deleteSurface")
            {
                surfaces.Remove(surfaceId);
                continue;
            }
            if (key == "updateDataModel")
            {
                if (body.TryGetPropertyValue("path", out var path) && (!OuroComposedCatalog.IsString(path, out var pointer) || (pointer != "" && !OuroPointer.IsData(pointer))))
                    w.Add($"{at}/updateDataModel/path", "path is a JSON pointer starting with /.");
                if (!body.ContainsKey("value")) w.Add($"{at}/updateDataModel/value", "updateDataModel needs value (null deletes).");
                continue;
            }
            if (!body.ContainsKey("components") && key == "createSurface") continue;
            if (body["components"] is not JsonArray components)
            {
                w.Add($"{at}/{key}/components", "components is a list.");
                continue;
            }
            var byId = surfaces[surfaceId];
            for (var j = 0; j < components.Count; j++)
            {
                var cpath = $"{at}/{key}/components/{j}";
                CheckComponent(w, components[j], cpath);
                if (components[j] is JsonObject c && OuroComposedCatalog.IsString(c["id"], out var id)) byId[id] = (c, cpath);
            }
        }
        // The tree each surface ends with: every child resolves, one root, no cycles.
        foreach (var (surfaceId, byId) in surfaces)
        {
            if (byId.Count == 0) continue;
            if (!byId.ContainsKey("root")) problems.Add(new(Failed, "", "One component must have id root; it is what the surface shows.", surfaceId));
            foreach (var (comp, path) in byId.Values)
                foreach (var (child, at) in ChildRefs(comp, path))
                {
                    if (!byId.ContainsKey(child)) problems.Add(new(Failed, at, $"No component has id {child}.", surfaceId));
                    if (child == "root") problems.Add(new(Failed, at, "root can't be a child.", surfaceId));
                }
            if (byId.ContainsKey("root")) Loops(problems, surfaceId, byId);
        }
        return problems;
    }

    // Core's depth-first walk for loops, from root, with its own stack instead of the call stack, and the depth limit.
    private static void Loops(List<OuroA2UIProblem> problems, string surfaceId, OrderedDictionary<string, (JsonObject Comp, string Path)> byId)
    {
        var seen = new HashSet<string>();
        var stack = new List<string>();
        var onStack = new HashSet<string>();
        var frames = new Stack<(string Id, List<(string Child, string At)> Children, int Next)>();
        var deep = false;
        bool Enter(string id)
        {
            if (onStack.Contains(id))
            {
                problems.Add(new(Failed, byId[id].Path, $"The tree loops: {string.Join(" > ", stack.Append(id))}.", surfaceId));
                return false;
            }
            if (seen.Contains(id) || !byId.ContainsKey(id)) return false;
            if (stack.Count >= MaxDepth)
            {
                if (!deep) problems.Add(new(Failed, byId[id].Path, $"The tree is more than {MaxDepth} components deep at {id}; nest less.", surfaceId));
                deep = true;
                return false;
            }
            stack.Add(id);
            onStack.Add(id);
            frames.Push((id, ChildRefs(byId[id].Comp, ""), 0));
            return true;
        }
        Enter("root");
        while (frames.Count > 0)
        {
            var (id, children, next) = frames.Pop();
            if (next < children.Count)
            {
                frames.Push((id, children, next + 1));
                Enter(children[next].Child);
                continue;
            }
            seen.Add(id);
            stack.RemoveAt(stack.Count - 1);
            onStack.Remove(id);
        }
    }

    private static List<(string Child, string At)> ChildRefs(JsonObject comp, string path)
    {
        var refs = new List<(string, string)>();
        var v = comp["children"];
        if (v is JsonArray list)
        {
            for (var i = 0; i < list.Count; i++) if (OuroComposedCatalog.IsString(list[i], out var c)) refs.Add((c, $"{path}/children/{i}"));
        }
        else if (v is JsonObject template && OuroComposedCatalog.IsString(template["componentId"], out var id))
        {
            refs.Add((id, $"{path}/children/componentId"));
        }
        return refs;
    }

    private static bool IsCall(JsonNode? v) => v is JsonObject o && o.ContainsKey("@call");

    private enum Dynamic { String, Strings, Boolean, Value }

    private static void CheckDynamic(Walk w, JsonNode? v, Dynamic kind, string path)
    {
        if (IsCall(v))
        {
            w.Add(path, "This catalog has no functions; bind to the data model with {\"@path\"} or give the value.");
            return;
        }
        if (OuroComposed.IsBinding(v)) return;
        var ok = kind switch
        {
            Dynamic.String => OuroComposedCatalog.IsString(v, out _),
            Dynamic.Boolean => v is JsonValue b && b.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
            Dynamic.Strings => v is JsonArray list && list.All(s => OuroComposedCatalog.IsString(s, out _)),
            _ => !(v is JsonObject o && o.Any(p => p.Key.StartsWith('@'))),
        };
        if (!ok)
        {
            var expected = kind switch { Dynamic.Strings => "a list of strings", Dynamic.Value => "a value", Dynamic.Boolean => "a boolean", _ => "a string" };
            w.Add(path, $"Expected {expected} or {{\"@path\"}}, got {Json(v)}.");
        }
    }

    private static void CheckInput(Walk w, OuroCatalogAction action, string id, bool given, JsonNode? input, string path)
    {
        if (action.PersonOnly is { } po)
        {
            if (given && !(input is JsonObject empty && empty.Count == 0)) w.Add(path, Summary(po), (JsonObject)po.Refusal.DeepClone());
            return;
        }
        if (given && input is not JsonObject)
        {
            w.Add(path, "input is an object of field values.");
            return;
        }
        var values = input as JsonObject ?? new JsonObject();
        foreach (var (name, v) in JsEntries(values))
        {
            var at = $"{path}/{name}";
            if (!action.Input.TryGetValue(name, out var f))
            {
                w.Add(at, $"{id} has no input {name}; its fields are {(action.Input.Count > 0 ? string.Join(", ", action.Input.Keys) : "none")}.");
                continue;
            }
            if (IsCall(v)) CheckDynamic(w, v, Dynamic.Value, at);
            if (OuroComposed.IsBinding(v) || IsCall(v)) continue;
            var ok = f.Kind switch
            {
                "string" => OuroComposedCatalog.IsString(v, out _),
                "boolean" => v is JsonValue b && b.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
                "enum" => OuroComposedCatalog.IsString(v, out var e) && (f.Values ?? []).Contains(e),
                _ => v is JsonValue n && n.GetValueKind() == JsonValueKind.Number && (f.Kind == "number" || IsInteger(n)),
            };
            if (!ok)
            {
                var kind = f.Kind switch { "enum" => $"one of {string.Join(", ", f.Values ?? [])}", "integer" => "an integer", _ => $"a {f.Kind}" };
                w.Add(at, $"{f.Label} is {kind}, got {Json(v)}.");
            }
        }
        foreach (var (name, f) in action.Input)
            if (!f.Optional && !values.ContainsKey(name)) w.Add(path, $"{id} needs {name} ({f.Label}) prepared before the person can press it.");
    }

    private static bool IsInteger(JsonValue n)
    {
        var d = n.TryGetValue<double>(out var x) ? x : double.Parse(n.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
        return double.IsFinite(d) && Math.Floor(d) == d;
    }

    // Every pointer a component names (a binding, or a template's path) that would reach an object's prototype.
    private static void UnsafePointers(Walk w, JsonNode? v, string path)
    {
        if (v is JsonArray list)
        {
            for (var i = 0; i < list.Count; i++) UnsafePointers(w, list[i], $"{path}/{i}");
            return;
        }
        if (v is not JsonObject o) return;
        string? pointer = OuroComposed.IsBinding(o) ? o["@path"]!.GetValue<string>()
            : OuroComposedCatalog.IsString(o["componentId"], out _) && OuroComposedCatalog.IsString(o["path"], out var p) ? p
            : null;
        if (pointer is not null && !OuroPointer.IsSafe(pointer)) w.Add(path, $"{Quote(pointer)} points into the data model, never through __proto__, constructor or prototype.");
        foreach (var (k, x) in JsEntries(o)) UnsafePointers(w, x, $"{path}/{k}");
    }

    private static void CheckComponent(Walk w, JsonNode? node, string path)
    {
        UnsafePointers(w, node, path);
        if (node is not JsonObject comp)
        {
            w.Add(path, "A component is an object with id and component.");
            return;
        }
        var hasId = comp.TryGetPropertyValue("id", out var idNode);
        if (!OuroComposedCatalog.IsString(idNode, out var id) || !IsIdentifier(id))
            w.Add($"{path}/id", $"id {(hasId ? Json(idNode) : "undefined")} is not an identifier: letters, digits and _, not starting with a digit (user_card, not user-card).");
        var hasComponent = comp.TryGetPropertyValue("component", out var componentNode);
        var isName = OuroComposedCatalog.IsString(componentNode, out var component);
        if (!isName || !w.Cat.Components.TryGetValue(component, out var spec))
        {
            w.Add($"{path}/component", $"{(hasComponent ? Json(componentNode) : "undefined")} is not in this catalog; use one of {string.Join(", ", w.Cat.Components.Keys)}.");
            return;
        }
        if (comp.TryGetPropertyValue("@catalogId", out var catalogId) && !(OuroComposedCatalog.IsString(catalogId, out var cid) && cid == w.Cat.CatalogId))
            w.Add($"{path}/@catalogId", $"This renderer has only the catalog {w.Cat.CatalogId}.");
        foreach (var (key, _) in JsEntries(comp))
        {
            if (key is "id" or "component" or "@catalogId" or "@metadata" or "@accessibility") continue;
            if (!spec.Props.ContainsKey(key)) w.Add($"{path}/{key}", $"{component} has no {key}{(spec.Props.Count > 0 ? $"; its props are {string.Join(", ", spec.Props.Keys)}" : "")}.");
        }
        if (comp["@accessibility"] is JsonObject accessibility)
            foreach (var (k, v) in JsEntries(accessibility))
            {
                if (k is "label" or "description") CheckDynamic(w, v, Dynamic.String, $"{path}/@accessibility/{k}");
                else if (k == "hidden") CheckDynamic(w, v, Dynamic.Boolean, $"{path}/@accessibility/{k}");
            }
        foreach (var (key, p) in spec.Props)
        {
            var at = $"{path}/{key}";
            if (!comp.TryGetPropertyValue(key, out var v))
            {
                if (p.Required) w.Add(at, $"{component} needs {key}: {p.Description}");
                continue;
            }
            switch (p.Kind)
            {
                case OuroPropKind.String:
                    CheckDynamic(w, v, Dynamic.String, at);
                    break;
                case OuroPropKind.Strings:
                    CheckDynamic(w, v, Dynamic.Strings, at);
                    break;
                case OuroPropKind.Boolean:
                    CheckDynamic(w, v, Dynamic.Boolean, at);
                    break;
                case OuroPropKind.Enum:
                    if (!OuroComposedCatalog.IsString(v, out var e) || !p.Values!.Contains(e)) w.Add(at, $"{key} is one of {string.Join(", ", p.Values!)}, got {Json(v)}.");
                    break;
                case OuroPropKind.Level:
                    if (!(v is JsonValue n && n.GetValueKind() == JsonValueKind.Number && OuroComposed.AsText(n) is "1" or "2" or "3")) w.Add(at, $"level is 1, 2 or 3, got {Json(v)}.");
                    break;
                case OuroPropKind.Children:
                    if (v is JsonArray children)
                    {
                        for (var i = 0; i < children.Count; i++)
                            if (!OuroComposedCatalog.IsString(children[i], out _)) w.Add($"{at}/{i}", "A child is a component id.");
                    }
                    else if (!(v is JsonObject t && t.Count == 2 && OuroComposedCatalog.IsString(t["componentId"], out _) && OuroComposedCatalog.IsString(t["path"], out _)))
                    {
                        w.Add(at, "children is a list of component ids, or a template { componentId, path }.");
                    }
                    break;
                case OuroPropKind.Terms:
                    if (OuroComposed.IsBinding(v)) break;
                    if (v is not JsonArray terms)
                    {
                        w.Add(at, "items is a list of { term, detail }, or {\"@path\"} to one.");
                        break;
                    }
                    for (var i = 0; i < terms.Count; i++)
                    {
                        if (terms[i] is not JsonObject it || it.Any(pair => pair.Key is not ("term" or "detail")))
                        {
                            w.Add($"{at}/{i}", "Each item is { term, detail }.");
                            continue;
                        }
                        foreach (var k in new[] { "term", "detail" })
                        {
                            if (!it.TryGetPropertyValue(k, out var part)) w.Add($"{at}/{i}/{k}", $"Each item needs {k}.");
                            else CheckDynamic(w, part, Dynamic.String, $"{at}/{i}/{k}");
                        }
                    }
                    break;
                case OuroPropKind.Path:
                    if (!OuroComposed.IsBinding(v)) w.Add(at, $"{key} is {{\"@path\": \"/...\"}}: where the person's answer goes in the data model.");
                    break;
                case OuroPropKind.Action:
                    if (!OuroComposedCatalog.IsString(v, out var actionId) || !w.Cat.Actions.ContainsKey(actionId))
                        w.Add(at, $"{Json(v)} is not one of the app's actions: {string.Join(", ", w.Cat.Actions.Keys)}.");
                    break;
                case OuroPropKind.Input:
                    break;
            }
        }
        if (component == "ActionButton" && OuroComposedCatalog.IsString(comp["action"], out var action) && w.Cat.Actions.TryGetValue(action, out var a))
            CheckInput(w, a, action, comp.TryGetPropertyValue("input", out var input), input, $"{path}/input");
    }

    // An A2UI component id: Unicode's identifier rule, as core's /^[\p{XID_Start}_][\p{XID_Continue}]*$/u. ID_Start and
    // ID_Continue by general category and Unicode's Other_ID_Start and Other_ID_Continue code points, less those XID drops
    // because NFKC turns them into something that isn't one (UAX #31). .NET's Unicode tables are its own, so a letter new
    // to a later version may read differently than in a JavaScript engine.
    private static bool IsIdentifier(string s)
    {
        if (s.Length == 0) return false;
        var first = true;
        foreach (var r in s.EnumerateRunes())
        {
            var v = r.Value;
            var c = Rune.GetUnicodeCategory(r);
            var letter = c is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.LetterNumber
                || v is 0x1885 or 0x1886 or 0x2118 or 0x212E or 0x309B or 0x309C;
            var go = letter || c is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.ConnectorPunctuation
                || v is 0x00B7 or 0x0387 or (>= 0x1369 and <= 0x1371) or 0x19DA or 0x200C or 0x200D;
            // Neither XID_Start nor XID_Continue: Pattern_Syntax, and the NFKC exceptions.
            var never = v is 0x2E2F or 0x037A or 0x309B or 0x309C or (>= 0xFC5E and <= 0xFC63) or 0xFDFA or 0xFDFB or 0xFE70 or 0xFE72 or 0xFE74 or 0xFE76 or 0xFE78 or 0xFE7A or 0xFE7C or 0xFE7E;
            // XID_Continue, but not XID_Start.
            var notFirst = v is 0x0E33 or 0x0EB3 or 0xFF9E or 0xFF9F;
            if (never || (first ? !(letter || v == '_') || notFirst : !go)) return false;
            first = false;
        }
        return true;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // The resolver: what every renderer must show
    // -----------------------------------------------------------------------------------------------------------------

    private sealed class Surface(JsonNode? data)
    {
        public OrderedDictionary<string, JsonObject> Components { get; } = new();

        public JsonNode? Data { get; set; } = data;
    }

    // Applies a stream the validator accepted, in order: surfaces created, their components set by id, their data written
    // by JSON pointer, and deleted (core's applyMessages).
    private static OrderedDictionary<string, Surface> ApplyMessages(JsonArray messages)
    {
        var surfaces = new OrderedDictionary<string, Surface>();
        foreach (var m in messages.OfType<JsonObject>())
        {
            if (m["createSurface"] is JsonObject create)
            {
                var s = new Surface(create["dataModel"]?.DeepClone() ?? new JsonObject());
                foreach (var c in (create["components"] as JsonArray ?? []).OfType<JsonObject>()) s.Components[c["id"]!.GetValue<string>()] = c;
                surfaces[create["surfaceId"]!.GetValue<string>()] = s;
            }
            else if (m["updateComponents"] is JsonObject update)
            {
                var s = surfaces[update["surfaceId"]!.GetValue<string>()];
                foreach (var c in (update["components"] as JsonArray ?? []).OfType<JsonObject>()) s.Components[c["id"]!.GetValue<string>()] = c;
            }
            else if (m["updateDataModel"] is JsonObject write)
            {
                var s = surfaces[write["surfaceId"]!.GetValue<string>()];
                s.Data = OuroPointer.Set(s.Data, OuroComposedCatalog.IsString(write["path"], out var path) ? path : "/", write["value"]);
            }
            else if (m["deleteSurface"] is JsonObject delete)
            {
                surfaces.Remove(delete["surfaceId"]!.GetValue<string>());
            }
        }
        return surfaces;
    }

    // A pointer in a scope: absolute from the root, relative from the template item's path.
    private static string Scoped(string scope, string p) => p.StartsWith('/') ? p : p == "" ? scope : $"{scope}/{p}";

    // A pointer that starts at the data model's root.
    private static string Absolute(string p) => p.StartsWith('/') ? p : $"/{p}";

    private static string PathOf(JsonNode? binding) => binding!["@path"]!.GetValue<string>();

    private static JsonNode? Value(JsonNode? data, string scope, JsonNode? v) => OuroComposed.IsBinding(v) ? OuroPointer.Get(data, Scoped(scope, PathOf(v))) : v;

    /// <summary>
    /// Every surface a stream leaves, resolved into Ouro's composed tree (core's <c>resolveMessages</c>), or only the
    /// validator's problems when it has any. <paramref name="label"/> is the app's own name for an id a confirm sheet
    /// shows, as in <see cref="OuroComposed.ActionButtonProps"/>.
    /// </summary>
    public static OuroA2UIResolved ResolveMessages(OuroComposedCatalog catalog, JsonNode? messages, Func<OuroLabelContext, string?>? label = null)
    {
        var problems = ValidateMessages(catalog, messages);
        if (problems.Count > 0) return new([], problems);
        var surfaces = new List<OuroA2UISurface>();
        // In the order JavaScript lists a record's keys, as core's resolveMessages gives them: index-like ids first.
        foreach (var (id, s) in JsOrder(ApplyMessages((JsonArray)messages!)))
        {
            try
            {
                surfaces.Add(new(id, ResolveSurface(catalog, s, label), s.Data?.DeepClone()));
            }
            catch (TooBig big)
            {
                return new([], [new(Failed, "", $"Surface {id} {big.Message}", id)]);
            }
        }
        return new(surfaces, []);
    }

    private sealed class TooBig(string message) : Exception(message) { }

    // What a surface has copied so far, against MaxSize. A value is walked only as far as the budget allows, so the walk
    // itself stays bounded.
    private sealed class Size
    {
        private long left = MaxSize;

        public JsonNode? Take(JsonNode? v)
        {
            var todo = new List<JsonNode?> { v };
            while (todo.Count > 0)
            {
                var x = todo[^1];
                todo.RemoveAt(todo.Count - 1);
                left -= 1;
                switch (x)
                {
                    case JsonObject o:
                        foreach (var (k, y) in o)
                        {
                            left -= k.Length + 1;
                            todo.Add(y);
                        }
                        break;
                    case JsonArray a:
                        left -= a.Count;
                        todo.AddRange(a);
                        break;
                    case JsonValue t when t.GetValueKind() == JsonValueKind.String:
                        left -= t.GetValue<string>().Length;
                        break;
                }
                if (left < 0) throw new TooBig($"resolves to more than {MaxSize} characters of data; show less of it, or fewer list items.");
            }
            return v;
        }
    }

    private static JsonObject? ResolveSurface(OuroComposedCatalog cat, Surface surface, Func<OuroLabelContext, string?>? label)
    {
        if (!surface.Components.TryGetValue("root", out var root)) return null;
        var data = surface.Data;
        var nodes = 0;
        var size = new Size();

        JsonObject Node(JsonObject comp, string scope)
        {
            if (++nodes > MaxNodes) throw new TooBig($"resolves to more than {MaxNodes} components; show fewer, or fewer list items.");
            var name = comp["component"]!.GetValue<string>();
            var spec = cat.Components[name];
            var props = new JsonObject();
            var children = new JsonArray();
            foreach (var (key, p) in spec.Props)
            {
                var given = comp.TryGetPropertyValue(key, out var raw);
                switch (p.Kind)
                {
                    case OuroPropKind.String:
                        if (given || p.Required) props[key] = Text(given ? size.Take(Value(data, scope, raw)) : null);
                        break;
                    case OuroPropKind.Strings:
                        props[key] = size.Take(Value(data, scope, raw)) is JsonArray items ? new JsonArray(items.Select(x => (JsonNode)Text(x)).ToArray()) : new JsonArray();
                        break;
                    case OuroPropKind.Boolean:
                        var on = given ? Value(data, scope, raw) is JsonValue b && b.GetValueKind() == JsonValueKind.True : p.Default is true;
                        props[key] = on;
                        break;
                    case OuroPropKind.Enum:
                        props[key] = OuroComposedCatalog.IsString(raw, out var word) ? word : (string)p.Default!;
                        break;
                    case OuroPropKind.Level:
                        props[key] = raw is JsonValue n && n.GetValueKind() == JsonValueKind.Number ? JsCopy(n) : JsonValue.Create((int)p.Default!);
                        break;
                    case OuroPropKind.Terms:
                    {
                        var bound = OuroComposed.IsBinding(raw);
                        var basePath = bound ? Scoped(scope, PathOf(raw)) : scope;
                        props[key] = size.Take(Value(data, scope, raw)) is JsonArray terms
                            ? new JsonArray(terms.Select((it, i) =>
                            {
                                var at = bound ? $"{basePath}/{i}" : scope;
                                return (JsonNode)new JsonObject
                                {
                                    ["term"] = Text(size.Take(Value(data, at, it is JsonObject o ? o["term"] : null))),
                                    ["detail"] = Text(size.Take(Value(data, at, it is JsonObject d ? d["detail"] : null))),
                                };
                            }).ToArray())
                            : new JsonArray();
                        break;
                    }
                    case OuroPropKind.Children:
                        if (raw is JsonArray ids)
                        {
                            foreach (var id in ids)
                                if (surface.Components.TryGetValue(OuroComposed.AsText(id), out var c)) children.Add(Node(c, scope));
                        }
                        else if (raw is JsonObject template)
                        {
                            var basePath = Scoped(scope, OuroComposed.AsText(template["path"]));
                            if (OuroPointer.Get(data, basePath) is JsonArray list && surface.Components.TryGetValue(OuroComposed.AsText(template["componentId"]), out var tpl))
                                for (var i = 0; i < list.Count; i++) children.Add(Node(tpl, $"{basePath}/{i}"));
                        }
                        break;
                    case OuroPropKind.Path:
                        props[key] = OuroComposed.IsBinding(raw) ? Absolute(Scoped(scope, PathOf(raw))) : "";
                        break;
                    case OuroPropKind.Action:
                    case OuroPropKind.Input:
                        break;
                }
            }
            if (name == "ActionButton")
            {
                // Bindings stay bindings here; once the tree is whole, Live reads those no field writes and adds the catalog's props.
                var id = comp["action"]!.GetValue<string>();
                var input = new JsonObject();
                if (cat.Actions[id].PersonOnly is null && comp["input"] is JsonObject prepared)
                    foreach (var (k, v) in JsEntries(prepared))
                        input[k] = OuroComposed.IsBinding(v) ? new JsonObject { ["@path"] = Absolute(Scoped(scope, PathOf(v))) } : JsCopy(size.Take(v));
                props["action"] = id;
                props["input"] = input;
            }
            if (comp["@accessibility"] is JsonObject accessibility && accessibility.TryGetPropertyValue("label", out var spoken))
                props["@label"] = Text(size.Take(Value(data, scope, spoken)));
            return new JsonObject { ["id"] = OuroComposed.AsText(comp["id"]), ["component"] = name, ["props"] = props, ["children"] = children };
        }

        return Live(cat, Node(root, ""), data, label, size);
    }

    // The tree with each button input bound to a path no field writes read from the data now, as before fields existed;
    // one bound to a field the person fills in stays bound, so a press sends their answer (OuroComposed.ResolveButton).
    private static JsonObject Live(OuroComposedCatalog cat, JsonObject tree, JsonNode? data, Func<OuroLabelContext, string?>? label, Size size)
    {
        var written = new HashSet<string>();
        void Collect(JsonObject n)
        {
            if (OuroComposed.InputComponents.Contains(n["component"]!.GetValue<string>()) && OuroComposedCatalog.IsString(n["props"]!["value"], out var path)) written.Add(path);
            foreach (var c in n["children"]!.AsArray()) Collect(c!.AsObject());
        }
        Collect(tree);
        void Fix(JsonObject n)
        {
            foreach (var c in n["children"]!.AsArray()) Fix(c!.AsObject());
            var props = n["props"]!.AsObject();
            if (n["component"]!.GetValue<string>() != "ActionButton" || props["input"] is not JsonObject prepared) return;
            var input = new JsonObject();
            foreach (var (k, v) in prepared)
                input[k] = OuroComposed.IsBinding(v) && !written.Contains(PathOf(v)) ? Read(PathOf(v)) : v?.DeepClone();
            var action = props["action"]!.GetValue<string>();
            var spoken = props.TryGetPropertyValue("@label", out var l) ? l?.DeepClone() : null;
            var hasLabel = props.ContainsKey("@label");
            // The catalog's props, computed once per button, so the app's label resolver is asked once per field; the
            // sender's @label goes last.
            var next = new JsonObject();
            foreach (var (k, v) in props)
                if (k is not ("action" or "input" or "@label")) next[k] = v?.DeepClone();
            foreach (var (k, v) in OuroComposed.ActionButtonProps(cat, action, input, label)) next[k] = v?.DeepClone();
            if (hasLabel) next["@label"] = spoken;
            n["props"] = next;
        }
        // A value read from the data is a value: one shaped like a binding is sent as its text, never followed.
        JsonNode? Read(string p)
        {
            var v = size.Take(OuroPointer.Get(data, p));
            return OuroComposed.IsBinding(v) ? Text(v) : JsCopy(v);
        }
        Fix(tree);
        return tree;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // A press, as A2UI's action message
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The A2UI <c>action</c> message a person's press sends (core's <c>actionMessage</c>): the action's id, the input with
    /// each field's answer read at the press, the label the person read, and that the person pressed it (and confirmed, for
    /// a consequential action). <paramref name="sourceComponentId"/> is the pressed button's id.
    /// </summary>
    public static JsonObject ActionMessage(string surfaceId, string sourceComponentId, OuroPersonPress press, string timestamp)
    {
        var consent = new JsonObject { ["consent"] = OuroComposed.ConsentName(press.Consent), ["by"] = press.By };
        if (press.Confirmed) consent["confirmed"] = true;
        return new JsonObject
        {
            ["version"] = Version,
            ["action"] = new JsonObject
            {
                ["name"] = press.Action,
                ["surfaceId"] = surfaceId,
                ["sourceComponentId"] = sourceComponentId,
                ["timestamp"] = timestamp,
                ["userMessage"] = press.Label,
                ["context"] = press.Input.DeepClone(),
                ["metadata"] = new JsonObject { ["extensions"] = new JsonObject { [ActionExtension] = consent } },
            },
        };
    }

    /// <summary>The action message for a pressed button, with each bound input read from <paramref name="data"/>.</summary>
    /// <exception cref="InvalidOperationException">The button hands off, or confirms and the person hasn't.</exception>
    public static JsonObject ActionMessage(string surfaceId, OuroComposedNode button, string timestamp, bool confirmed = false, JsonNode? data = null) =>
        ActionMessage(surfaceId, button.Id, OuroComposed.PersonPress(button, confirmed, data), timestamp);

    /// <summary>A timestamp as A2UI's action message carries it, and as JavaScript's <c>toISOString</c> writes it.</summary>
    public static string Timestamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    // -----------------------------------------------------------------------------------------------------------------
    // Text, as JavaScript writes it
    // -----------------------------------------------------------------------------------------------------------------

    // A value as text (core's asText): numbers and booleans as written, null and missing as "", others as JSON.
    private static string Text(JsonNode? v) => v is JsonObject or JsonArray ? Json(v) : OuroComposed.AsText(v);

    // JSON.stringify, so a problem or a value's text reads the same on every kit.
    internal static string Json(JsonNode? v) => v switch
    {
        null => "null",
        JsonObject o => "{" + string.Join(",", JsEntries(o).Select(p => Quote(p.Key) + ":" + Json(p.Value))) + "}",
        JsonArray a => "[" + string.Join(",", a.Select(Json)) + "]",
        JsonValue x => x.GetValueKind() switch
        {
            JsonValueKind.String => Quote(x.GetValue<string>()),
            // JSON.stringify writes a number that isn't finite as null; parsed JSON never holds one.
            JsonValueKind.Number => OuroComposed.AsText(x) is "NaN" or "Infinity" or "-Infinity" ? "null" : OuroComposed.AsText(x),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "null",
        },
        _ => "null",
    };

    // A JavaScript object lists its keys that are array indexes first, in ascending order, then the rest as they were
    // added; JSON.parse, Object.keys and JSON.stringify all follow it.
    private static bool IsIndex(string key) =>
        key == "0" || (key.Length is > 0 and <= 10 && key[0] is >= '1' and <= '9' && key.All(char.IsAsciiDigit) && ulong.Parse(key, CultureInfo.InvariantCulture) < uint.MaxValue);

    private static IEnumerable<KeyValuePair<string, T>> JsOrder<T>(IEnumerable<KeyValuePair<string, T>> pairs)
    {
        var list = pairs.ToList();
        return list.Where(p => IsIndex(p.Key)).OrderBy(p => ulong.Parse(p.Key, CultureInfo.InvariantCulture)).Concat(list.Where(p => !IsIndex(p.Key)));
    }

    private static IEnumerable<KeyValuePair<string, JsonNode?>> JsEntries(JsonObject o) => JsOrder(o);

    // A value as JavaScript holds it once parsed: keys in its order and each number as JSON.stringify writes it, so a tree
    // reads byte for byte as core's.
    private static JsonNode? JsCopy(JsonNode? v) => v switch
    {
        null => null,
        JsonObject o => new JsonObject(JsEntries(o).Select(p => KeyValuePair.Create(p.Key, JsCopy(p.Value)))),
        JsonArray a => new JsonArray(a.Select(JsCopy).ToArray()),
        JsonValue n when n.GetValueKind() == JsonValueKind.Number => JsonNode.Parse(Json(n)),
        _ => v.DeepClone(),
    };

    private static string Quote(string s)
    {
        var b = new StringBuilder(s.Length + 2).Append('"');
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            switch (c)
            {
                case '"': b.Append("\\\""); break;
                case '\\': b.Append("\\\\"); break;
                case '\b': b.Append("\\b"); break;
                case '\f': b.Append("\\f"); break;
                case '\n': b.Append("\\n"); break;
                case '\r': b.Append("\\r"); break;
                case '\t': b.Append("\\t"); break;
                default:
                    var lone = char.IsHighSurrogate(c) ? i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]) : char.IsLowSurrogate(c) && (i == 0 || !char.IsHighSurrogate(s[i - 1]));
                    if (c < 0x20 || lone) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else b.Append(c);
                    break;
            }
        }
        return b.Append('"').ToString();
    }
}
