// The Windows X-ray: does what agents can call agree with what people press? It reads the window's UI Automation tree,
// the live MCP server's answers, the App Actions the installed package registered (and, where the machine has the App
// Actions runtime, the system catalog), and what the App Action door showed. Each door's expectation comes from core's
// doorsOf, so the X-ray and the generator can't disagree about what a door carries.
// Usage: node tools/xray.mts <actions.mts> <evidence dir>. Exit 1 when they disagree.
import { readFile, writeFile } from "node:fs/promises";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";
import { isDeepStrictEqual } from "node:util";
import { compareMcp, names, refusal, type ActionSet } from "../vendor/core/ouro-actions.mts";
import { doorsOf } from "../vendor/core/ouro-actions-windows.mts";

const [decl, dir] = process.argv.slice(2);
if (!decl || !dir) {
  process.stderr.write("usage: node tools/xray.mts <actions.mts> <evidence dir>\n");
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
const ui = await read("uia-light.json");
const mcp = await read("mcp-probe.json");
const confirm = await read("confirm-mcp.json");
const registered = await read("registered-app-actions.json");
const catalog = await read("catalog.json");
const uriRefused = await read("uia-uri-person-only.json");
const uriRan = await read("uia-uri-write.json");

type Row = { check: string; action: string; result: "pass" | "fail" | "not run"; detail: string };
const rows: Row[] = [];
const row = (check: string, action: string, ok: boolean | null, detail: string) => rows.push({ check, action, result: ok === null ? "not run" : ok ? "pass" : "fail", detail });
const statusOf = (dump: any): string | null => (dump?.elements ?? []).find((e: any) => e.automationId === "status")?.name ?? null;

const buttons = (ui?.elements ?? []).filter((e: any) => e.controlType === "ControlType.Button" && String(e.automationId).startsWith("action."));
const tools: any[] = mcp?.toolsList?.tools ?? [];
const appActions: any[] = registered?.actions ?? [];
const catalogIds: string[] | null = catalog?.ok ? (catalog.actions as any[]).map((a) => a.id) : null;

for (const a of set.actions) {
  const doors = doorsOf(a);
  const button = buttons.find((b: any) => b.automationId === `action.${a.id}`);
  row("people press it", a.id, ui ? Boolean(button && button.name === a.label && button.helpText === a.description) : null, button ? `button "${button.name}", hint "${button.helpText}"` : "no button");
  const tool = tools.find((t) => t.name === a.id);
  const appAction = appActions.find((x) => x.id === `Ouro.${names(a.id).pascal}`);
  const answer = mcp?.calls?.[a.id];
  row(doors.mcp.carries ? "MCP tool listed" : "no MCP tool", a.id, mcp ? Boolean(tool) === doors.mcp.carries : null, tool ? "listed" : "not listed");
  row(doors.appAction.carries ? "App Action registered" : "no App Action", a.id, registered ? Boolean(appAction) === doors.appAction.carries : null, appAction ? appAction.invocation.uri : doors.appAction.carries ? "not registered" : `none: ${doors.appAction.why}`);
  if (catalogIds) row("system catalog matches", a.id, catalogIds.some((id) => id.endsWith(`Ouro.${names(a.id).pascal}`)) === doors.appAction.carries, `${catalogIds.length} actions in the catalog`);
  if (a.consent === "person-only") {
    row("MCP call answers core's refusal", a.id, mcp ? Boolean(answer?.isError === true && isDeepStrictEqual(answer.structuredContent, refusal(a))) : null, answer ? JSON.stringify(answer.structuredContent?.error?.code ?? answer) : "no call");
    const shown = statusOf(uriRefused);
    row("App Action launch refused", a.id, uriRefused ? typeof shown === "string" && shown.startsWith("Refused at the AppAction door") && shown.includes(refusal(a).summary) : null, shown ?? "no status");
  } else if (a.consent === "consequential") {
    const asked = confirm?.seen === true && confirm.defaultFocus === "No";
    row("MCP call asks the person first (No is the default)", a.id, confirm ? asked : null, confirm ? `dialog "${confirm.title}": ${(confirm.text ?? []).join(" ")}; focus on ${confirm.defaultFocus}` : "no dialog seen");
    row("person's No means nothing ran", a.id, answer ? answer.isError === true && answer.structuredContent?.error?.code === "declined" : null, answer ? JSON.stringify(answer.structuredContent?.error?.code ?? answer.structuredContent) : "no call");
  } else {
    row("MCP call runs it", a.id, answer ? answer.isError === false : null, answer ? JSON.stringify(answer.structuredContent) : "no call");
    const retry = mcp?.calls?.[`${a.id}_retry`];
    if (retry) row("a retry with the same key runs once", a.id, isDeepStrictEqual(retry.structuredContent, answer?.structuredContent), `${JSON.stringify(answer?.structuredContent)} then ${JSON.stringify(retry.structuredContent)}`);
    const shown = statusOf(uriRan);
    if (a.id === "add_shopping_list_item") row("App Action launch runs it", a.id, uriRan ? typeof shown === "string" && shown.includes("through the AppAction door") : null, shown ?? "no status");
  }
}
for (const b of buttons) if (!set.actions.some((a) => `action.${a.id}` === b.automationId)) row("no undeclared button", b.automationId, false, b.name);
if (mcp) {
  const drift = compareMcp(set, mcp.toolsList);
  row("core compare-mcp on the live tools/list", "*", drift.length === 0, drift.join("; ") || "no drift");
}
if (!catalog?.ok) row("system App Actions catalog", "*", null, catalog ? `unavailable on this machine: ${catalog.error} (${catalog.os})` : "not probed");

const failed = rows.filter((r) => r.result === "fail");
const table = ["| Check | Action | Result | Detail |", "| --- | --- | --- | --- |", ...rows.map((r) => `| ${r.check} | \`${r.action}\` | ${r.result} | ${String(r.detail).replace(/\|/g, "\\|").slice(0, 200)} |`)].join("\n");
await writeFile(join(dir, "xray.json"), `${JSON.stringify({ agree: failed.length === 0, rows }, null, 2)}\n`);
await writeFile(join(dir, "xray.md"), `## X-ray: agents and people agree: ${failed.length ? "NO" : "yes"}\n\n${table}\n`);
process.stdout.write(`${table}\n`);
process.exit(failed.length ? 1 : 0);
