// The spike's MCP server over stdio (newline-delimited JSON-RPC 2.0), served from the generated declarations:
// tools/list is core's mcp.json, and a call to a person-only action answers core's typed refusal.
// In the package it is registered with the On-device Agent Registry through the com.microsoft.windows.ai.mcpServer
// app extension and the generated MCP bundle manifest.
using System.Text.Json;
using System.Text.Json.Nodes;
using Ouro.Actions.Generated;
using Ouro.Shop;

ShoppingHandler.Bind();
var stdout = Console.OpenStandardOutput();
var tools = JsonNode.Parse(OuroActions.McpToolsJson)!.AsArray();
string? line;
while ((line = Console.ReadLine()) is not null)
{
    if (string.IsNullOrWhiteSpace(line)) continue;
    JsonObject request;
    try { request = JsonNode.Parse(line)!.AsObject(); }
    catch (JsonException) { await Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = null, ["error"] = new JsonObject { ["code"] = -32700, ["message"] = "parse error" } }); continue; }
    var id = request["id"]?.DeepClone();
    var method = request["method"]?.GetValue<string>();
    if (id is null) continue; // a notification
    JsonNode? result = null;
    JsonObject? error = null;
    switch (method)
    {
        case "initialize":
            result = new JsonObject
            {
                ["protocolVersion"] = request["params"]?["protocolVersion"]?.GetValue<string>() ?? "2025-06-18",
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                ["serverInfo"] = new JsonObject { ["name"] = "ouro-shop-spike", ["version"] = "0.1.0" },
            };
            break;
        case "ping":
            result = new JsonObject();
            break;
        case "tools/list":
            result = new JsonObject { ["tools"] = tools.DeepClone() };
            break;
        case "tools/call":
            var name = request["params"]?["name"]?.GetValue<string>() ?? "";
            var arguments = request["params"]?["arguments"] as JsonObject ?? new JsonObject();
            if (OuroActions.Find(name) is null) { error = new JsonObject { ["code"] = -32602, ["message"] = $"unknown tool {name}" }; break; }
            var outcome = await OuroActions.InvokeAsync(name, (JsonObject)arguments.DeepClone(), OuroDoor.Mcp);
            var refused = outcome["state"]?.GetValue<string>() == "refused";
            result = new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = outcome.ToJsonString() }),
                ["structuredContent"] = outcome.DeepClone(),
                ["isError"] = refused,
            };
            break;
        default:
            error = new JsonObject { ["code"] = -32601, ["message"] = $"method not found: {method}" };
            break;
    }
    var response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id };
    if (error is not null) response["error"] = error; else response["result"] = result;
    await Send(response);
}

async Task Send(JsonObject message)
{
    var bytes = System.Text.Encoding.UTF8.GetBytes(message.ToJsonString() + "\n");
    await stdout.WriteAsync(bytes);
    await stdout.FlushAsync();
}
