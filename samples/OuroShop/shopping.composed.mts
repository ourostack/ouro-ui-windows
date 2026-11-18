// A card Ouro Shop's agent composes for the person: Ouro's composed tree, carried in an MCP tool result as
// structuredContent.ouro, against the catalog core generates from the sample's actions (Generated/catalog.json). Every
// button is one of the declared actions, so its label, consent and confirm sheet come from the catalog
// (actionButtonProps); the agent picks the action, the input and the emphasis, and nothing else.
//   node samples/OuroShop/shopping.composed.mts generate   write Generated/composed.json
//   node samples/OuroShop/shopping.composed.mts check      fail when it drifted (CI's generated job)
import { readFile, writeFile } from "node:fs/promises";
import { actionButtonProps, type ComposedNode, type Json, type Obj } from "../../vendor/core/ouro-composed.mts";
import { loadCatalog } from "../../vendor/core/ouro-a2ui.mts";
import { mcpAppTreeResult } from "../../vendor/core/ouro-mcp-app.mts";

const here = (p: string) => new URL(p, import.meta.url);
const catalog = loadCatalog(JSON.parse(await readFile(here("Generated/catalog.json"), "utf8")));
const target = here("Generated/composed.json");

const node = (id: string, component: string, props: Obj, children: ComposedNode[] = []): ComposedNode => ({ id, component, props, children });
const row = (id: string, gap: string, children: ComposedNode[]) => node(id, "Stack", { direction: "horizontal", gap }, children);
const button = (id: string, action: string, input: Obj, emphasis = "secondary") => node(id, "ActionButton", { emphasis, ...actionButtonProps(catalog, action, input) });
const field = (id: string, label: string, value: string, hint: string | undefined, type = "text") =>
  node(id, "TextField", { label, value, ...(hint ? { hint } : {}), type, multiline: false });

const tree = row("root", "large", [
  node("week", "Card", {}, [
    node("photo", "Image", { url: "https://raw.githubusercontent.com/ourostack/.github/main/brand/headers/ouro-ui-windows.webp", description: "Ouro basking on a cabin windowsill while a small robot opens one pane", aspect: "wide" }),
    node("title", "Heading", { text: "This week's list", level: 1 }),
    row("tags", "small", [node("count", "Badge", { text: "3 items", tone: "neutral" }), node("vegan", "Badge", { text: "Vegan", tone: "success" })]),
    node("why", "Text", { text: "Picked from the recipes you cooked this month.", tone: "muted" }),
    node("details", "DescriptionList", { items: [{ term: "Delivery", detail: "Saturday" }, { term: "Store", detail: "Corner Grocer" }] }),
  ]),
  node("items", "Card", {}, [
    node("pick", "Heading", { text: "To pick up", level: 2 }),
    node("things", "List", { items: ["Oat milk", "Eggs", "Lemons"], ordered: false }),
    node("line", "Separator", {}),
    node("steps", "List", { items: ["Check the fridge", "Add what's missing", "Place the order"], ordered: true }),
    node("low", "Alert", { title: "Lemons are low", text: "Add some before Saturday.", tone: "warning" }),
  ]),
  node("plan", "Card", {}, [
    node("add", "Heading", { text: "Add something", level: 2 }),
    field("item", "Item", "/item", "As you'd say it, such as oat milk."),
    field("quantity", "Quantity", "/quantity", undefined, "number"),
    node("organic", "Checkbox", { label: "Only organic", value: "/organic" }),
    node("store", "Select", { label: "Store", value: "/store", options: ["Corner Grocer", "Market Hall", "Any"] }),
    field("share_with", "Share with", "/share/with", "An email address."),
    row("actions", "small", [
      button("add_item", "add_shopping_list_item", { name: { "@path": "/item" }, quantity: { "@path": "/quantity" } }, "primary"),
      button("share", "share_shopping_list", { with: { "@path": "/share/with" } }),
      button("order", "place_grocery_order", {}),
    ]),
  ]),
]);

const data: Obj = { item: "oat milk", quantity: 2, organic: false, store: "Corner Grocer", share: { with: "sam@example.com" } };
const result = mcpAppTreeResult({ text: "This week's list: oat milk, eggs and lemons, delivered Saturday.", catalog, tree, data });
const text = `${JSON.stringify(result as unknown as Json, null, 2)}\n`;

const [cmd] = process.argv.slice(2);
if (cmd === "generate") {
  await writeFile(target, text);
  process.stdout.write("shopping.composed: wrote Generated/composed.json.\n");
} else if (cmd === "check") {
  const have = await readFile(target, "utf8").catch(() => null);
  if (have !== text) {
    process.stderr.write(`shopping.composed: Generated/composed.json ${have === null ? "is missing" : "drifted from its source"}. Fix: node samples/OuroShop/shopping.composed.mts generate; never edit it by hand.\n`);
    process.exit(1);
  }
  process.stdout.write("shopping.composed: Generated/composed.json matches its source and validates against the catalog.\n");
} else {
  process.stderr.write("usage: node samples/OuroShop/shopping.composed.mts generate | check\n");
  process.exit(2);
}
