// A synthetic action set shaped like a shopping list, for the Windows spike: one write action that agents may call,
// and one person-only action that every agent door refuses. Not any app's real code or data.
import { defineAction, defineActions, field } from "./vendor/ouro-actions.mts";

export const addShoppingListItem = defineAction({
  id: "add_shopping_list_item",
  consent: "write",
  label: "Add to list",
  description: "Add an item to your shopping list.",
  agent: "Add one item to the person's shopping list. Give the name as the person said it; leave quantity out when they gave none.",
  input: {
    name: field.string({ label: "Item", description: "What to buy, as the person said it, such as \"oat milk\".", arg: true, ask: "What should I add to your list?" }),
    quantity: field.number({ label: "Quantity", description: "How many or how much.", optional: true, min: 0 }),
  },
  output: {
    id: field.string({ label: "Item id", description: "The new item's id." }),
  },
  cli: "list add",
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

export default defineActions("shopping", [addShoppingListItem, placeGroceryOrder], { tool: "shop" });
