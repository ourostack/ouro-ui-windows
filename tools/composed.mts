// Judges Ouro Shop's composed page: does the surface draw the agent's card as the composed tree says, to UI Automation as
// well as on screen, and does each button follow its consent class? It reads the page's UI Automation dump
// (uia-composed-light.json, from tools/dump.ps1), the card itself (samples/OuroShop/Composed/card.json) and what
// tools/composed.ps1 recorded as it typed, ticked, chose and pressed, and as the agent updated the card
// (composed-presses.json).
// Usage: node tools/composed.mts <evidence dir>. Exit 1 when any check fails or can't run.
import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { isDeepStrictEqual } from "node:util";

const [dir] = process.argv.slice(2);
if (!dir) {
  process.stderr.write("usage: node tools/composed.mts <evidence dir>\n");
  process.exit(2);
}
const json = async (p: string | URL) => {
  try {
    return JSON.parse((await readFile(p, "utf8")).replace(/^\uFEFF/, ""));
  } catch {
    return null;
  }
};
type Node = { id: string; component: string; props: Record<string, any>; children: Node[] };
const card = (await json(new URL("../samples/OuroShop/Composed/card.json", import.meta.url)))?.structuredContent?.ouro;
const tree = card.tree as Node;
const dump = await json(join(dir, "uia-composed-light.json"));
const presses = await json(join(dir, "composed-presses.json"));

type Row = { check: string; node: string; result: "pass" | "fail" | "not run"; detail: string };
const rows: Row[] = [];
const row = (check: string, node: string, ok: boolean | null, detail: string) => rows.push({ check, node, result: ok === null ? "not run" : ok ? "pass" : "fail", detail });

const all = (n: Node): Node[] => [n, ...n.children.flatMap(all)];
const firstHeading = (n: Node): string | undefined => (n.component === "Heading" ? n.props.text : n.children.map(firstHeading).find(Boolean));
const at = (data: any, pointer: string) => pointer.slice(1).split("/").reduce((v, k) => (v == null ? undefined : v[k.replace(/~1/g, "/").replace(/~0/g, "~")]), data);
const text = (v: unknown) => (v === undefined || v === null ? "" : typeof v === "string" ? v : typeof v === "object" ? JSON.stringify(v) : String(v));

const elements: any[] = dump?.elements ?? [];
const inside = (a: any, b: any) => Boolean(a && b) && a.x >= b.x - 1 && a.y >= b.y - 1 && a.x + a.width <= b.x + b.width + 1 && a.y + a.height <= b.y + b.height + 1;
const find = (id: string, type: string) => elements.find((e) => e.automationId === id && e.controlType === `ControlType.${type}`) ?? elements.find((e) => e.automationId === id);
const items = (list: any) => elements.filter((e) => e.controlType === "ControlType.ListItem" && inside(e.rect, list?.rect)).map((e) => e.name);
const headingLevels = elements.some((e) => e.headingLevel !== null && e.headingLevel !== undefined);

/** What UI Automation must read for each component the card uses: its control type, its name and what else it says. */
const expect = (n: Node): { type: string; ok: (e: any) => boolean; says: string } | null => {
  const p = n.props;
  switch (n.component) {
    case "Card":
      return { type: "Group", ok: (e) => e.name === firstHeading(n), says: `a Group named "${firstHeading(n)}"` };
    case "Heading":
      return { type: "Text", ok: (e) => e.name === p.text && e.headingLevel === p.level, says: `Text "${p.text}", heading level ${p.level}` };
    case "Text":
    case "Badge":
      return { type: "Text", ok: (e) => e.name === p.text, says: `Text "${p.text}"` };
    case "Image":
      return { type: "Image", ok: (e) => e.name === p.description, says: `an Image named "${p.description}"` };
    case "List":
      return { type: "List", ok: (e) => isDeepStrictEqual(items(e), p.items), says: `a List of ${p.items.join(", ")}` };
    case "DescriptionList": {
      const pairs = p.items.map((t: any) => `${t.term}, ${t.detail}`);
      return { type: "List", ok: (e) => isDeepStrictEqual(items(e), pairs), says: `a List of ${pairs.join("; ")}` };
    }
    case "Separator":
      return { type: "Separator", ok: () => true, says: "a Separator" };
    case "Alert":
      return { type: "Group", ok: (e) => e.name === p.title && e.helpText === (p.text ?? ""), says: `a Group named "${p.title}", help "${p.text ?? ""}"` };
    case "TextField":
      return { type: "Edit", ok: (e) => e.name === p.label && e.helpText === (p.hint ?? "") && e.value === text(at(card.data, p.value)), says: `an Edit named "${p.label}" holding "${text(at(card.data, p.value))}"` };
    case "Checkbox":
      return { type: "CheckBox", ok: (e) => e.name === p.label && e.toggleState === (at(card.data, p.value) === true ? "On" : "Off"), says: `a CheckBox named "${p.label}"` };
    case "Select":
      return { type: "ComboBox", ok: (e) => e.name === p.label, says: `a ComboBox named "${p.label}"` };
    case "ActionButton": {
      const help = p.press === "handoff" ? p.handoff : p.press === "confirm" ? p.confirm.description : "";
      return { type: "Button", ok: (e) => e.name === p.label && e.helpText === help && e.isKeyboardFocusable, says: `a Button named "${p.label}", help "${help}"` };
    }
    default:
      return null;
  }
};

if (!dump) row("UI Automation reads the card", "*", null, "no uia-composed-light.json");
else {
  const surface = find("composed.surface", "Group");
  row("the surface is named by the card's first heading", "root", surface ? surface.controlType === "ControlType.Group" && surface.name === firstHeading(tree) : false, surface ? `${surface.controlType} "${surface.name}"` : "not found");
  for (const n of all(tree)) {
    const want = expect(n);
    if (!want) continue;
    const e = find(`composed.${n.id}`, want.type);
    if (n.component === "Heading" && !headingLevels) {
      row(`${n.component} reads as ${want.says}`, n.id, null, "this machine's UI Automation client reads no heading levels");
      continue;
    }
    row(`${n.component} reads as ${want.says}`, n.id, Boolean(e && e.controlType === `ControlType.${want.type}` && want.ok(e)), e ? `${e.controlType} "${e.name}"${e.helpText ? `, help "${e.helpText}"` : ""}${e.value != null ? `, value "${e.value}"` : ""}${e.toggleState ? `, ${e.toggleState}` : ""}${e.headingLevel ? `, level ${e.headingLevel}` : ""}` : "not found");
  }
  const components = new Set(all(tree).map((n) => n.component));
  row("the card uses every catalog component", "*", components.size === 14, [...components].join(", "));
}

const step = (name: string) => (presses?.steps ?? []).find((s: any) => s.step === name);
const data = (s: any) => {
  try {
    return JSON.parse(s?.data ?? "");
  } catch {
    return null;
  }
};
if (!presses) row("a person presses", "*", null, "no composed-presses.json");
else {
  const [start, answered, run, cancelled, confirmed, handoff, updated, refused, corrected] = ["start", "answered", "run", "cancelled", "confirmed", "handoff", "updated", "refused", "corrected"].map(step);
  row("fields start from the data model", "*", isDeepStrictEqual(data(start), card.data), start?.data ?? "no step");
  const want = { ...card.data, item: "eggs", quantity: 3, organic: true, store: "Market Hall" };
  row("fields write the person's answers into the data model, and nothing runs", "*", isDeepStrictEqual(data(answered), want) && answered?.auditLines === start?.auditLines, `${answered?.data}; audit ${start?.auditLines} → ${answered?.auditLines}`);
  row("a write button runs at once with the bound answers", "add_item", run?.status === 'Add to list pressed with {"name":"eggs","quantity":3}: Done' && run?.auditLines === answered?.auditLines + 1, `${run?.status}; audit +${run?.auditLines - answered?.auditLines}`);
  const d = presses.dialog;
  row("a consequential button asks first, showing each value under its label", "share", Boolean(d && d.primary === "Share list" && d.close === "Cancel" && isDeepStrictEqual(d.rows, ["With, pat@example.com"]) && d.texts.includes("Share list?") && d.texts.includes("Share your shopping list with someone in your household.")), d ? `"${d.primary}" or "${d.close}"; rows ${JSON.stringify(d.rows)}` : "no dialog");
  row("the confirm dialog's default isn't the yes", "share", d ? d.focused !== d.primary : null, d ? `focus on "${d.focused}"` : "no dialog");
  row("Cancel sends nothing", "share", Boolean(cancelled && cancelled.status === run?.status && cancelled.auditLines === run?.auditLines), `${cancelled?.status}; audit +${cancelled?.auditLines - run?.auditLines}`);
  row("the confirm sends what the person entered, confirmed", "share", confirmed?.status === 'Share list, confirmed, pressed with {"with":"pat@example.com"}: Done' && confirmed?.auditLines === cancelled?.auditLines + 1, `${confirmed?.status}; audit +${confirmed?.auditLines - cancelled?.auditLines}`);
  const kept = { ...card.data, item: "bread", quantity: 6, organic: true, store: "Market Hall", share: { with: "pat@example.com" } };
  const shown = (s: any) => `title "${s?.title}", alert "${s?.low}", item "${s?.item}", quantity "${s?.quantity}", problem "${s?.problem}"; ${s?.status}; ${s?.data}`;
  row("an update changes the card, and keeps what the person typed except where the agent set the value", "title, low, /quantity", Boolean(updated && updated.status === "Update 1 applied." && updated.title === "This week's list, for six" && updated.low === "Lemons are back in stock" && updated.item === "bread" && updated.quantity === "6" && !updated.problem && isDeepStrictEqual(data(updated), kept) && updated.auditLines === handoff?.auditLines), shown(updated));
  row("an update the card can't take shows the short alert, applies nothing and keeps the card hidden", "subtitle", Boolean(refused && refused.status.startsWith('Update 2 refused: /nodes/0/id "subtitle" is not in the tree') && refused.problem === "This card can't be shown" && !refused.title && isDeepStrictEqual(data(refused), kept)), shown(refused));
  row("a corrected update applies to the last card, with what the person typed", "title, low", Boolean(corrected && corrected.status === "Update 3 applied." && !corrected.problem && corrected.title === "This week's list, for six" && corrected.item === "bread" && isDeepStrictEqual(data(corrected), kept)), shown(corrected));
  row("a person-only button hands off and never presses", "order", Boolean(handoff && handoff.status === "Handed off: Place order is done with Place order on the shopping list. Nothing was sent." && handoff.auditLines === confirmed?.auditLines), `${handoff?.status}; audit +${handoff?.auditLines - confirmed?.auditLines}`);
}

const failed = rows.filter((r) => r.result !== "pass");
const table = ["| Check | Node | Result | Detail |", "| --- | --- | --- | --- |", ...rows.map((r) => `| ${r.check.replace(/\|/g, "\\|")} | \`${r.node}\` | ${r.result} | ${String(r.detail).replace(/\|/g, "\\|").slice(0, 240)} |`)].join("\n");
await writeFile(join(dir, "composed.json"), `${JSON.stringify({ ok: failed.length === 0, rows }, null, 2)}\n`);
await writeFile(join(dir, "composed.md"), `## The composed tree on Windows: ${failed.length ? "NO" : "drawn and pressed as the tree says"}\n\n${table}\n`);
process.stdout.write(`${table}\n`);
process.exit(failed.length ? 1 : 0);
