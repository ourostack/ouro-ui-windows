// A card Ouro Shop's agent composes for the person: Ouro's composed tree, carried in an MCP tool result as
// structuredContent.ouro, against the catalog core generates from the sample's actions (Composed/catalog.json). Every
// button is one of the declared actions, so its label, consent and confirm sheet come from the catalog
// (actionButtonProps); the agent picks the action, the input and the emphasis, and nothing else. Then two updates the
// agent sends to that card (structuredContent.ouro.update): one that changes a heading, an alert and the quantity, and
// one for a node the card doesn't have, which the view refuses. And the same card as A2UI messages
// (structuredContent.a2ui), which core's A2UI adapter must resolve to exactly the same tree and data.
//   node samples/OuroShop/shopping.composed.mts generate   write Composed/card.json, update.json, update-missing.json and card.a2ui.json
//   node samples/OuroShop/shopping.composed.mts check      fail when they drifted (CI's generated job)
import { readFile, writeFile } from "node:fs/promises";
import { isDeepStrictEqual } from "node:util";
import { actionButtonProps, COMPONENTS, type ComposedNode, type Json, type Obj } from "../../vendor/core/ouro-composed.mts";
import { A2UI_VERSION, loadCatalog, mcpAppToolResult, resolveMessages } from "../../vendor/core/ouro-a2ui.mts";
import { mcpAppTreeResult, mcpAppTreeUpdateResult } from "../../vendor/core/ouro-mcp-app.mts";

const here = (p: string) => new URL(p, import.meta.url);
const catalogJson = JSON.parse(await readFile(here("Composed/catalog.json"), "utf8"));
const catalog = loadCatalog(catalogJson);

const node = (id: string, component: string, props: Obj, children: ComposedNode[] = []): ComposedNode => ({ id, component, props, children });
const row = (id: string, gap: string, children: ComposedNode[]) => node(id, "Stack", { direction: "horizontal", gap }, children);
const button = (id: string, action: string, input: Obj, emphasis = "secondary") => node(id, "ActionButton", { emphasis, ...actionButtonProps(catalog, action, input) });
const field = (id: string, label: string, value: string, hint: string | undefined, type = "text") =>
  node(id, "TextField", { label, value, ...(hint ? { hint } : {}), type, multiline: false });

const tree = row("root", "large", [
  node("week", "Card", {}, [
    node("photo", "Image", { url: "https://raw.githubusercontent.com/ourostack/.github/main/brand/og/ouro-ui-windows.jpg", description: "Ouro basking on a cabin windowsill while a small robot opens one pane", aspect: "wide" }),
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
const update = mcpAppTreeUpdateResult({
  text: "Lemons are back, and the list is for six.",
  catalog,
  update: {
    nodes: [node("title", "Heading", { text: "This week's list, for six", level: 1 }), node("low", "Alert", { title: "Lemons are back in stock", text: "Add some before Saturday.", tone: "success" })],
    data: [{ path: "/quantity", value: 6 }],
  },
});
// The sender checks only what needs no tree; the view finds no "subtitle" in the card and refuses the whole update.
const missing = mcpAppTreeUpdateResult({ text: "Serves six.", catalog, update: { nodes: [node("subtitle", "Text", { text: "Serves six", tone: "muted" })] } });
// The same card as an agent writes it in A2UI: one flat list of components, each naming its children by id, a field's
// value bound to the data model with {"@path"}, and a button naming only its action, its input and its emphasis.
const component = (n: ComposedNode): Obj => {
  if (n.component === "ActionButton") return { id: n.id, component: n.component, action: n.props.action!, input: n.props.input!, emphasis: n.props.emphasis! };
  const props = COMPONENTS[n.component]!.props;
  const out: Obj = { id: n.id, component: n.component };
  for (const [key, value] of Object.entries(n.props)) out[key] = props[key]?.type.kind === "path" ? { "@path": value } : value;
  if (props.children) out.children = n.children.map((c) => c.id);
  return out;
};
const flat = (n: ComposedNode): ComposedNode[] => [n, ...n.children.flatMap(flat)];
const messages: Json[] = [
  { version: A2UI_VERSION, createSurface: { surfaceId: "list", catalogId: catalog.catalogId, dataModel: data } },
  { version: A2UI_VERSION, updateComponents: { surfaceId: "list", components: flat(tree).map(component) } },
];
const a2ui = mcpAppToolResult({ text: "This week's list: oat milk, eggs and lemons, delivered Saturday.", catalog: catalogJson, messages });
const resolved = resolveMessages(catalogJson, messages);
if (!("surfaces" in resolved) || !isDeepStrictEqual(resolved.surfaces.list, tree)) {
  process.stderr.write("shopping.composed: the A2UI card doesn't resolve to the composed card's tree. Fix: change component() so it does.\n");
  process.exit(1);
}

const files = new Map([
  ["Composed/card.json", result],
  ["Composed/update.json", update],
  ["Composed/update-missing.json", missing],
  ["Composed/card.a2ui.json", a2ui],
].map(([name, value]) => [name as string, `${JSON.stringify(value as unknown as Json, null, 2)}\n`]));

const [cmd] = process.argv.slice(2);
if (cmd === "generate") {
  for (const [name, text] of files) await writeFile(here(name), text);
  process.stdout.write(`shopping.composed: wrote ${[...files.keys()].join(", ")}.\n`);
} else if (cmd === "check") {
  for (const [name, text] of files) {
    const have = await readFile(here(name), "utf8").catch(() => null);
    if (have !== text) {
      process.stderr.write(`shopping.composed: ${name} ${have === null ? "is missing" : "drifted from its source"}. Fix: node samples/OuroShop/shopping.composed.mts generate; never edit it by hand.\n`);
      process.exit(1);
    }
  }
  process.stdout.write("shopping.composed: the card and its updates match their source and pass the sender's checks.\n");
} else {
  process.stderr.write("usage: node samples/OuroShop/shopping.composed.mts generate | check\n");
  process.exit(2);
}
