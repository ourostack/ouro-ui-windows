// Ouro Shop's one handler. The buttons, the App Actions and the MCP server all reach it through OuroActionRuntime,
// which has already checked the input, refused a person-only action at an agent door and asked the person about a
// consequential one. Synthetic: it keeps the list in memory.
using System.Text.Json.Nodes;
using Ouro.Actions;

namespace OuroShop;

public static class ShoppingHandler
{
    private static int next;

    public static Task<JsonObject> Handle(OuroActionInfo action, JsonObject input, CancellationToken cancellation) => action.Id switch
    {
        "add_shopping_list_item" => Task.FromResult(new JsonObject { ["id"] = $"item_{Interlocked.Increment(ref next)}" }),
        "share_shopping_list" => Task.FromResult(new JsonObject()),
        "place_grocery_order" => Task.FromResult(new JsonObject()),
        _ => throw new ArgumentException($"{action.Id}: the handler has no case for it."),
    };
}
