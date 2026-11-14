// The Windows X-ray: does what agents can call agree with what people press? It reads the window's UI Automation tree,
// the live MCP server's answers, the App Actions the installed package registered (and, when the machine has the App
// Actions runtime, the system catalog), and what the App Action door showed when a person-only action was launched.
// Usage: node xray.mts <actions.mts> <evidence dir>. Exit 1 when they disagree.
import { readFile, writeFile } from "node:fs/promises";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";
import { isDeepStrictEqual } from "node:util";
import { compareMcp, refusal, type ActionSet } from "./vendor/ouro-actions.mts";
import { appActionGap } from "./windows-actions.mts";

const [decl, dir] = process.argv.slice(2);
if (!decl || !dir) {
  process.stderr.write("usage: node xray.mts <actions.mts> <evidence dir>\n");
  process.exit(2);
}
const set = ((await import(pathToFileURL(resolve(decl)).href)) as { default: ActionSet }).default;
const read = async (name: string) => {
  try {
    return JSON.parse((await readFile(join(dir, name), "utf8")).replace(/^﻿/, ""));
  } catch {
    return null;
  }
};
const ui = await read("uia-default.json");
const mcp = await read("mcp-probe-packaged.json") ?? await read("mcp-probe.json");
const registered = await read("registered-app-actions.json");
const catalog = await read("catalog.json");
const uriOrder = await read("uia-uri-place_grocery_order.json");
const uriAdd = await read("uia-uri-add_shopping_list_item.json");

type Row = { check: string; action: string; result: "pass" | "fail" | "not run"; detail: string };
const rows: Row[] = [];
const row = (check: string, action: string, ok: boolean | null, detail: string) => rows.push({ check, action, result: ok === null ? "not run" : ok ? "pass" : "fail", detail });

const elements: any[] = ui?.elements ?? [];
const buttons = elements.filter((e) => e.controlType === "ControlType.Button" && String(e.automationId).startsWith("action."));
const tools: any[] = mcp?.toolsList?.tools ?? [];
const appActions: any[] = registered?.actions ?? [];
const catalogIds: string[] | null = catalog?.ok ? (catalog.actions as any[]).map((a) => a.id) : null;

for (const a of set.actions) {
  const button = buttons.find((b) => b.automationId === `action.${a.id}`);
  row("people press it", a.id, ui ? Boolean(button && button.name === a.label) : null, button ? `button "${button.name}"` : "no button");
  const tool = tools.find((t) => t.name === a.id);
  const appAction = appActions.find((x) => x.id === `Ouro.${a.names.pascal}`);
  if (a.consent === "person-only") {
    row("no MCP tool", a.id, mcp ? !tool : null, tool ? "listed" : "not listed");
    const answer = mcp?.calls?.[a.id];
    row("MCP call refused", a.id, mcp ? Boolean(answer?.isError === true && isDeepStrictEqual(answer.structuredContent, refusal(a))) : null, answer ? JSON.stringify(answer.structuredContent?.error?.code ?? answer) : "no call");
    row("no App Action", a.id, registered ? !appAction : null, appAction ? "registered" : "not registered");
    if (catalogIds) row("not in the system catalog", a.id, !catalogIds.some((id) => id.endsWith(`Ouro.${a.names.pascal}`)), `${catalogIds.length} actions in the catalog`);
    const shown = (uriOrder?.elements ?? []).find((e: any) => e.automationId === "status")?.name ?? null;
    row("App Action door refuses a launch", a.id, uriOrder ? typeof shown === "string" && shown.startsWith("Refused at the AppAction door") && shown.includes(refusal(a).summary) : null, shown ?? "no status");
  } else {
    row("MCP tool", a.id, mcp ? Boolean(tool) : null, tool ? "listed" : "not listed");
    const gap = appActionGap(a);
    if (gap) row("App Action", a.id, null, `none by design: ${gap}`);
    else {
      row("App Action registered", a.id, registered ? Boolean(appAction && String(appAction.invocation?.uri).includes(`/action/${a.id}?`)) : null, appAction ? appAction.invocation.uri : "not registered");
      if (catalogIds) row("in the system catalog", a.id, catalogIds.some((id) => id.endsWith(`Ouro.${a.names.pascal}`)), `${catalogIds.length} actions in the catalog`);
      const shown = (uriAdd?.elements ?? []).find((e: any) => e.automationId === "status")?.name ?? null;
      row("App Action door runs it", a.id, uriAdd ? typeof shown === "string" && shown.includes("through the AppAction door") : null, shown ?? "no status");
    }
  }
}
for (const b of buttons) if (!set.actions.some((a) => `action.${a.id}` === b.automationId)) row("no undeclared button", b.automationId, false, b.name);
if (mcp) {
  const drift = compareMcp(set, mcp.toolsList);
  row("core compare-mcp on the live tools/list", "*", drift.length === 0, drift.join("; ") || "no drift");
}
if (!catalog?.ok) row("system App Actions catalog", "*", null, catalog ? `unavailable on this machine: ${catalog.error} (${catalog.os})` : "not probed");

const failed = rows.filter((r) => r.result === "fail");
const table = ["| Check | Action | Result | Detail |", "| --- | --- | --- | --- |", ...rows.map((r) => `| ${r.check} | \`${r.action}\` | ${r.result} | ${String(r.detail).replace(/\|/g, "\\|").slice(0, 160)} |`)].join("\n");
await writeFile(join(dir, "xray.json"), `${JSON.stringify({ agree: failed.length === 0, rows }, null, 2)}\n`);
await writeFile(join(dir, "xray.md"), `## X-ray: agents and people agree ${failed.length ? "NO" : "yes"}\n\n${table}\n`);
process.stdout.write(`${table}\n`);
process.exit(failed.length ? 1 : 0);
