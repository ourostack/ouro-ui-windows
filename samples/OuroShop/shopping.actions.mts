// Ouro Shop's actions: a synthetic shopping list, one action per agent-relevant consent class. Every door in the
// sample (its buttons, its App Actions, its MCP server) is generated from these declarations:
//   node vendor/core/ouro-actions-windows.mts generate samples/OuroShop/shopping.actions.mts samples/OuroShop/Generated
import { defineAction, defineActions, field } from "../../vendor/core/ouro-actions.mts";

export const addShoppingListItem = defineAction({
  id: "add_shopping_list_item",
  consent: "write",
  label: "Add to list",
  description: "Add an item to your shopping list.",
  agent: "Add one item to the person's shopping list. Give the name as the person said it; leave quantity out when they gave none.",
  input: {
    name: field.string({ label: "Item", description: "What to buy, as the person said it, such as \"oat milk\".", arg: true, ask: "What should I add to your list?", maxLength: 80 }),
    quantity: field.number({ label: "Quantity", description: "How many or how much.", optional: true, min: 0 }),
    idempotencyKey: field.string({ label: "Idempotency key", description: "Send the same key again when retrying this exact call, and the item is added once.", optional: true, protocolOnly: true }),
  },
  output: {
    id: field.string({ label: "Item id", description: "The new item's id." }),
  },
  cli: "list add",
});

export const shareShoppingList = defineAction({
  id: "share_shopping_list",
  consent: "consequential",
  label: "Share list",
  description: "Share your shopping list with someone in your household.",
  agent: "Share the person's shopping list with another person, who can then see and change it. The person confirms first.",
  input: {
    with: field.string({ label: "With", description: "The email address of the person to share with.", arg: true, format: "email" }),
  },
  output: {},
  cli: "list share",
});

export const placeGroceryOrder = defineAction({
  id: "place_grocery_order",
  consent: "person-only",
  label: "Place order",
  description: "Order everything on your list and pay for it.",
  agent: "Order the shopping list's items for delivery and pay for them.",
  input: {},
  output: {},
  personOnly: {
    reason: "You can't place an order or pay for the person; only they can.",
    how: "with Place order on the shopping list",
  },
  cli: "list order",
});

export default defineActions("shopping", [addShoppingListItem, shareShoppingList, placeGroceryOrder], { tool: "shop" });

/** The package's URI scheme and MCP server, for the Windows projection. */
export const windows = { scheme: "ouro-shop", server: "OuroShop.Mcp", version: "0.1.0", namespace: "OuroShop.Actions" };
