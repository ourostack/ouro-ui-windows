// What ouro-ui-core's Windows projection (ouro-actions-windows.mts) describes: each action and the catalog of them.
// OuroActions.g.cs builds these from an app's declarations; nothing here is specific to one app.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ouro.Actions;

/// <summary>What an action needs before it runs, as core's <c>defineAction</c> declares it.</summary>
public enum OuroConsent
{
    /// <summary>Changes nothing.</summary>
    Read,
    /// <summary>Changes only the person's own things.</summary>
    Write,
    /// <summary>Reaches other people or can't be taken back: an agent may ask, and the person says yes before it runs.</summary>
    Consequential,
    /// <summary>Only the person starts it. Every agent door answers core's typed refusal.</summary>
    PersonOnly,
}

/// <summary>Which door a call came through.</summary>
public enum OuroDoor
{
    /// <summary>The person's own press in the app.</summary>
    Person,
    /// <summary>The app's MCP server, registered with the On-device Agent Registry.</summary>
    Mcp,
    /// <summary>A Windows App Action, which launches the app through its URI scheme.</summary>
    AppAction,
}

/// <summary>One declared action, as the generated <c>OuroActions.g.cs</c> describes it.</summary>
/// <param name="Id">The snake_case id: the MCP tool's name.</param>
/// <param name="Consent">What it needs before it runs.</param>
/// <param name="Label">The person's button text.</param>
/// <param name="Description">The person's one sentence: the control's help text.</param>
/// <param name="Agent">The agent's description: the MCP tool's description.</param>
/// <param name="InputSchemaJson">The input's JSON Schema, as MCP lists it.</param>
/// <param name="RefusalJson">Core's typed refusal, for a person-only action; otherwise null.</param>
/// <param name="AppAction">Whether the App Actions door carries it (core's doors.json says why not).</param>
/// <param name="Idempotent">Declared idempotent: calling it again with the same input changes nothing more.</param>
public sealed record OuroActionInfo(
    string Id,
    OuroConsent Consent,
    string Label,
    string Description,
    string Agent,
    string InputSchemaJson,
    string? RefusalJson,
    bool AppAction,
    bool Idempotent)
{
    private JsonObject? schema;

    /// <summary>The UI Automation id of the control a person presses for it, which the X-ray reads.</summary>
    public string AutomationId => "action." + Id;

    /// <summary>Whether any agent door may call it.</summary>
    public bool AgentsMayCall => Consent != OuroConsent.PersonOnly;

    /// <summary>The input schema, parsed once.</summary>
    public JsonObject InputSchema => schema ??= JsonNode.Parse(InputSchemaJson)!.AsObject();
}

/// <summary>An app's actions, its URI scheme and its MCP server, as the generated <c>OuroActions.Catalog</c>.</summary>
public sealed record OuroActionCatalog(string Set, string Scheme, string Server, string Version, string McpToolsJson, IReadOnlyList<OuroActionInfo> Actions)
{
    /// <summary>The action with this id, or null.</summary>
    public OuroActionInfo? Find(string id) => Actions.FirstOrDefault(a => a.Id == id);

    /// <summary>
    /// The question a person is asked before the action runs: the one its declaration gives as <c>confirm</c> (carried in the
    /// MCP tool's <c>_meta["ouro.bot/confirm"]</c>), else its description, so the prompt says what the button and the other
    /// doors say.
    /// </summary>
    public string ConfirmText(OuroActionInfo action)
    {
        foreach (var tool in JsonNode.Parse(McpToolsJson)!.AsArray())
            if (tool?["name"]?.GetValue<string>() == action.Id && tool["_meta"]?["ouro.bot/confirm"]?.GetValue<string>() is { Length: > 0 } confirm)
                return confirm;
        return action.Description;
    }
}

/// <summary>How the doors write JSON: as core writes it, without escaping apostrophes or non-ASCII text.</summary>
public static class OuroJson
{
    public static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The node as compact JSON.</summary>
    public static string Write(JsonNode node) => node.ToJsonString(Options);
}
