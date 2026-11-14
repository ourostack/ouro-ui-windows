// The two agent doors' own decoding, before the shared entry point: an App Action's URI launch, and an MCP server
// over stdio (newline-delimited JSON-RPC 2.0) whose tools/list is core's mcp.json.
using System.Text;
using System.Text.Json.Nodes;

namespace Ouro.Actions;

/// <summary>Reads an App Action's launch: <c>&lt;scheme&gt;://action/&lt;id&gt;?&lt;input&gt;=…&amp;token=…</c>.</summary>
public static class OuroAppActionUri
{
    /// <summary>
    /// The call an App Action's URI makes, or null when the URI isn't one of this app's action launches. Each query value
    /// is percent-decoded once; an empty value is left out (App Actions send an empty optional input). The invocation's
    /// token becomes the call's idempotency key, so a replayed launch runs once.
    /// </summary>
    public static OuroCall? Read(OuroActionCatalog catalog, Uri uri)
    {
        if (!string.Equals(uri.Scheme, catalog.Scheme, StringComparison.OrdinalIgnoreCase) || uri.Host != "action") return null;
        var id = uri.AbsolutePath.Trim('/');
        var input = new JsonObject();
        string? token = null;
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var name = Uri.UnescapeDataString(eq < 0 ? pair : pair[..eq]);
            var value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            if (name == "token") token = value;
            else if (value.Length > 0) input[name] = value;
        }
        return new OuroCall(id, input, OuroDoor.AppAction) { Caller = "app-action", IdempotencyKey = token is { Length: > 0 } ? "app-action:" + token : null };
    }
}

/// <summary>The app's MCP server, served from the catalog through the runtime. The package registers it with the On-device Agent Registry.</summary>
public sealed class OuroMcpServer(OuroActionRuntime runtime)
{
    private string? client;

    /// <summary>Serves requests from <paramref name="input"/> until it ends, one JSON-RPC message per line.</summary>
    public async Task RunAsync(Stream input, Stream output, CancellationToken cancellation = default)
    {
        using var reader = new StreamReader(input, new UTF8Encoding(false));
        while (await reader.ReadLineAsync(cancellation).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var response = await HandleAsync(line, cancellation).ConfigureAwait(false);
            if (response is null) continue;
            var bytes = Encoding.UTF8.GetBytes(OuroJson.Write(response) + "\n");
            await output.WriteAsync(bytes, cancellation).ConfigureAwait(false);
            await output.FlushAsync(cancellation).ConfigureAwait(false);
        }
    }

    /// <summary>The answer to one JSON-RPC message, or null for a notification.</summary>
    public async Task<JsonObject?> HandleAsync(string line, CancellationToken cancellation = default)
    {
        JsonObject request;
        try { request = JsonNode.Parse(line)!.AsObject(); }
        catch (Exception) { return Error(null, -32700, "parse error: send one JSON-RPC 2.0 object per line"); }
        var id = request["id"]?.DeepClone();
        if (id is null) return null;
        var method = request["method"]?.GetValue<string>();
        var p = request["params"] as JsonObject;
        switch (method)
        {
            case "initialize":
                client = p?["clientInfo"]?["name"]?.GetValue<string>();
                return Result(id, new JsonObject
                {
                    ["protocolVersion"] = p?["protocolVersion"]?.GetValue<string>() ?? "2025-06-18",
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                    ["serverInfo"] = new JsonObject { ["name"] = runtime.Catalog.Server, ["version"] = runtime.Catalog.Version },
                });
            case "ping":
                return Result(id, new JsonObject());
            case "tools/list":
                return Result(id, new JsonObject { ["tools"] = JsonNode.Parse(runtime.Catalog.McpToolsJson) });
            case "tools/call":
                var name = p?["name"]?.GetValue<string>() ?? "";
                if (runtime.Catalog.Find(name) is null) return Error(id, -32602, $"unknown tool \"{name}\". Fix: call a tool from tools/list.");
                var arguments = p?["arguments"] as JsonObject ?? new JsonObject();
                var key = p?["_meta"]?["ouro/idempotencyKey"]?.GetValue<string>();
                var outcome = await runtime.InvokeAsync(new OuroCall(name, (JsonObject)arguments.DeepClone(), OuroDoor.Mcp) { Caller = client ?? "mcp", IdempotencyKey = key is null ? null : "mcp:" + key }, cancellation).ConfigureAwait(false);
                return Result(id, new JsonObject
                {
                    ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = OuroJson.Write(outcome.Body) }),
                    ["structuredContent"] = outcome.Body.DeepClone(),
                    ["isError"] = outcome.State != OuroOutcomeState.Done,
                });
            default:
                return Error(id, -32601, $"method not found: {method}");
        }
    }

    private static JsonObject Result(JsonNode id, JsonNode result) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    private static JsonObject Error(JsonNode? id, int code, string message) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };
}
