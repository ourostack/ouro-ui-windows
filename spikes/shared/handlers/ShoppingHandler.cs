// The spike's one handler for the shopping actions. The button, the App Action and the MCP server all reach it
// through OuroActions.InvokeAsync, which refuses a person-only action at every agent door before it gets here.
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Ouro.Actions.Generated;

namespace Ouro.Shop;

public static class ShoppingHandler
{
    private static int next;

    public static void Bind() => OuroActions.Handler = Handle;

    public static Task<JsonObject> Handle(string id, JsonObject input) => id switch
    {
        "add_shopping_list_item" => Task.FromResult(new JsonObject { ["id"] = $"item_{Interlocked.Increment(ref next)}" }),
        "place_grocery_order" => Task.FromResult(new JsonObject()),
        _ => throw new System.ArgumentException($"{id}: the handler has no case for it."),
    };
}
