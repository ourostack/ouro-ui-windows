import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import { test } from "node:test";
import set, { placeGroceryOrder } from "./shopping.actions.mts";
import { refusal, renderActions } from "./vendor/ouro-actions.mts";
import { appActionGap, renderWindows, SPIKE_OPTIONS } from "./windows-actions.mts";

const files = renderWindows(set, SPIKE_OPTIONS);
const parsed = (name: string) => JSON.parse(files.get(name)!);

test("vendored core files match their release hashes", async () => {
  const vendor = JSON.parse(await readFile(new URL("./vendor/vendor.json", import.meta.url), "utf8"));
  for (const [name, { sha256 }] of Object.entries(vendor.files) as [string, { sha256: string }][]) {
    const bytes = await readFile(new URL(`./vendor/${name}`, import.meta.url));
    assert.equal(createHash("sha256").update(bytes).digest("hex"), sha256, name);
  }
});

test("App Actions register every agent action they can carry, and never a person-only one", () => {
  const ids = parsed("windows/app-actions.json").actions.map((a: { id: string }) => a.id);
  assert.deepEqual(ids, ["Ouro.AddShoppingListItem"]);
  assert.match(appActionGap(placeGroceryOrder)!, /person-only/);
  assert.deepEqual(parsed("windows/app-actions.skipped.json").skipped, { place_grocery_order: "person-only: no agent door offers it" });
});

test("the MCP bundle's static tools/list is core's mcp.json, so the registry and the live server can't differ", () => {
  const core = JSON.parse(renderActions(set).get("mcp.json")!);
  assert.deepEqual(parsed("windows/mcpb.manifest.json")._meta["com.microsoft.windows"].static_responses["tools/list"].tools, core.tools);
  assert.ok(!core.tools.some((t: { name: string }) => t.name === "place_grocery_order"));
});

test("the C# refusal is core's refusal, byte for byte", () => {
  assert.ok(files.get("windows/OuroActions.g.cs")!.includes(JSON.stringify(refusal(placeGroceryOrder))));
});

test("the committed generated files match the declarations", async () => {
  for (const [rel, text] of files) assert.equal(await readFile(new URL(`./generated/${rel}`, import.meta.url), "utf8"), text, rel);
});
