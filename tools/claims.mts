// Core's claims, run on Windows for every component the kit ships (KIT below). Each component spec in vendor/core/components
// names its claims; every one must have a Windows check here, so a claim core adds fails this until Windows proves it.
// The checks read UI Automation trees of the real window under the Windows settings each claim names (light, dark, a
// contrast theme, animations off, text size 225%), the controls' self-reports, and pixels sampled from screenshots.
// Usage: node tools/claims.mts <evidence dir>. Exit 1 when any claim fails or can't run.
import { readFile, readdir, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { wordsAsReported } from "./pixels.mts";

const [dir] = process.argv.slice(2);
if (!dir) {
  process.stderr.write("usage: node tools/claims.mts <evidence dir>\n");
  process.exit(2);
}
const json = async (p: string) => {
  try {
    return JSON.parse((await readFile(p, "utf8")).replace(/^﻿/, ""));
  } catch {
    return null;
  }
};
const manifest = await json(new URL("../samples/OuroShop/Generated/mcpb.manifest.json", import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1"));
/** The question the declaration asks before an action runs: the confirm text the MCP tool carries (core's actionText(action).confirm). */
const confirmOf = (id: string): string | undefined => {
  const find = (n: any): any => (n && typeof n === "object" ? (n.name === id && n._meta ? n : Object.values(n).map(find).find(Boolean)) : undefined);
  return find(manifest)?._meta?.["ouro.bot/confirm"];
};
const vendor = new URL("../vendor/core/", import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1");
const claims = (await json(join(vendor, "components.claims.json")))?.claims as { id: string; tier: string; appliesTo: string[] }[];
/** The components this kit ships. Core specs more; each one joins here when its control does, with a check for each claim. */
const KIT = ["button", "badge", "card", "item", "checkbox", "text-field", "separator", "description-list", "tabs", "tab-bar", "sidebar", "item-group", "dialog", "sheet", "confirm-dialog", "switch", "select"];
/** The panels that open over a page: each holds buttons of its own (close, cancel, confirm), which are its controls. */
const PANELS = ["dialog", "sheet", "confirm-dialog"];
const specs = (await Promise.all((await readdir(join(vendor, "components"))).filter((f) => f.endsWith(".spec.json")).map((f) => json(join(vendor, "components", f))))).filter((s: any) => KIT.includes(s?.name));
const missing = KIT.filter((k) => !specs.some((s: any) => s.name === k));
if (missing.length) {
  process.stderr.write(`claims: no vendored spec for ${missing.join(", ")}. Fix: node scripts/provenance.mjs update <core tag>.\n`);
  process.exit(1);
}

/** Every dump, by the setting it was taken under. Each holds the list page and the gallery's two pages. */
const settings = ["light", "dark", "contrast", "noanim", "text225"] as const;
type Setting = (typeof settings)[number];
const dumps = Object.fromEntries(await Promise.all(settings.map(async (s) => [s, [await json(join(dir, `uia-${s}.json`)), await json(join(dir, `uia-gallery-${s}.json`)), await json(join(dir, `uia-fields-${s}.json`)), await json(join(dir, `uia-nav-${s}.json`)), await json(join(dir, `uia-lists-${s}.json`)), await json(join(dir, `uia-overlays-${s}.json`)), await json(join(dir, `uia-confirms-${s}.json`)), await json(join(dir, `uia-forms-${s}.json`))].filter(Boolean)] as const))) as Record<Setting, any[]>;

const report = (e: any): Record<string, string> => {
  const m = /ouro:([\w-]+);(.*)$/.exec(String(e?.itemStatus ?? ""));
  return m ? { kind: m[1]!, ...Object.fromEntries(m[2]!.split(";").filter(Boolean).map((kv) => kv.split("=") as [string, string])) } : {};
};
/** The kit's controls of one kind under a setting, each with its self-report and the rest of its page, keyed by automation id or name. */
const of = (setting: Setting, kind: string) =>
  dumps[setting].flatMap((d) => (d.elements as any[]).map((e) => ({ e, r: report(e), dpi: (d.systemDpi ?? 96) / 96, page: d.elements as any[] })).filter((x) => x.r.kind === kind && (scope === undefined || belongs(x, scope))));
const key = (x: { e: any }) => x.e.automationId || x.e.name;
/** While set, `of` returns only the elements of one spec case: the gallery id it carries, or an item inside it. */
let scope: string | undefined;
const belongs = (x: { e: any; r: Record<string, string> }, id: string) => key(x) === id || key(x).startsWith(`${id}.`) || x.r.list === id;

const lum = (hex: string) => {
  const c = [3, 5, 7].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((v) => (v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4));
  return 0.2126 * c[0]! + 0.7152 * c[1]! + 0.0722 * c[2]!;
};
const ratio = (a: string, b: string) => {
  const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p);
  return (x! + 0.05) / (y! + 0.05);
};
const near = (a: string, b: string) => [3, 5, 7].every((i) => Math.abs(parseInt(a.slice(i, i + 2), 16) - parseInt(b.slice(i, i + 2), 16)) <= 10);

type Result = { ok: boolean | null; detail: string };
const none = (what: string): Result => ({ ok: null, detail: `no ${what} in the evidence` });
const all = (xs: { ok: boolean; why: string }[], what: string): Result =>
  xs.length === 0 ? none(what) : { ok: xs.every((x) => x.ok), detail: `${xs.filter((x) => x.ok).length}/${xs.length} ${what}${xs.some((x) => !x.ok) ? `; failing: ${xs.filter((x) => !x.ok).map((x) => x.why).slice(0, 6).join("; ")}` : ""}` };

/** Whether one element's box lies within another's, give or take a pixel. */
const inside = (a: any, b: any) => Boolean(a && b) && a.x >= b.x - 1 && a.y >= b.y - 1 && a.x + a.width <= b.x + b.width + 1 && a.y + a.height <= b.y + b.height + 1;
/** The buttons of a panel: the controls inside its box, by name. */
const buttonsIn = (page: any[], panel: any) => page.filter((b) => b.controlType === "ControlType.Button" && inside(b.rect, panel.rect));
const named = (page: any[], panel: any, name: string | undefined) => buttonsIn(page, panel).find((b) => b.name === name);
/** The panel's title is a level 2 heading and, when it has a description, its words are a text inside it. */
const headed = (page: any[], e: any) => e.name && page.some((h) => h.headingLevel === 2 && h.name === e.name && inside(h.rect, e.rect));
/** What UI Automation must see for each component (its spec's accessibility.windows). */
const spoken: Record<string, (x: { e: any; r: Record<string, string>; page: any[] }) => boolean> = {
  sidebar: ({ e, r, page }) => e.controlType === "ControlType.ListItem" && e.name === r.spokenName && tabList(page, r)?.controlType === "ControlType.List" && Boolean(tabList(page, r)?.name),
  "tab-bar": ({ e, r, page }) => e.controlType === "ControlType.ListItem" && Boolean(e.name) && tabList(page, r)?.controlType === "ControlType.List" && Boolean(tabList(page, r)?.name),
  tabs: ({ e, r, page }) => e.controlType === "ControlType.TabItem" && Boolean(e.name) && tabList(page, r)?.controlType === "ControlType.Tab" && Boolean(tabList(page, r)?.name),
  button: ({ e }) => e.controlType === "ControlType.Button" && Boolean(e.name) && e.isKeyboardFocusable,
  badge: ({ e }) => e.controlType === "ControlType.Text" && Boolean(e.name) && !e.isKeyboardFocusable,
  card: ({ e }) => e.controlType === "ControlType.Group" && Boolean(e.name),
  item: ({ e, r }) => (r.pressable === "true" ? e.controlType === "ControlType.Button" && e.isKeyboardFocusable && Boolean(e.helpText) : e.controlType === "ControlType.Group" && !e.isKeyboardFocusable) && Boolean(e.name),
  checkbox: ({ e, r }) =>
    e.controlType === "ControlType.CheckBox" && e.isKeyboardFocusable && Boolean(e.name) && e.toggleState === { on: "On", off: "Off", mixed: "Indeterminate" }[r.state!] && (r.required !== "true" || e.name.endsWith(", required")),
  switch: ({ e, r }) =>
    e.controlType === "ControlType.Button" && e.isKeyboardFocusable && Boolean(e.name) && e.toggleState === { on: "On", off: "Off" }[r.state!] && (r.described === "true") === Boolean(e.helpText) && (r.required !== "true" || e.name.endsWith(", required")),
  select: ({ e, r }) =>
    e.controlType === "ControlType.ComboBox" && e.isKeyboardFocusable && Boolean(e.name) && (r.described === "true") === Boolean(e.helpText) && (r.required !== "true" || e.name.endsWith(", required")),
  "text-field": ({ e, r }) => e.controlType === "ControlType.Edit" && e.isKeyboardFocusable && Boolean(e.name) && (r.described === "true") === Boolean(e.helpText) && (r.required !== "true" || e.name.endsWith(", required")),
  separator: ({ e, r }) => (r.labelled === "true" ? e.controlType === "ControlType.Text" && Boolean(e.name) : e.controlType === "ControlType.Separator") && !e.isKeyboardFocusable,
  dialog: ({ e, r, page }) => e.controlType === "ControlType.Window" && Boolean(e.name) && (r.hideTitle === "true" || Boolean(headed(page, e))) && Boolean(named(page, e, r.close)),
  sheet: ({ e, r, page }) => e.controlType === "ControlType.Pane" && Boolean(e.name) && (r.hideTitle === "true" || Boolean(headed(page, e))) && Boolean(named(page, e, r.close)),
  "confirm-dialog": ({ e, r, page }) =>
    e.controlType === "ControlType.Window" && Boolean(e.name) && Boolean(headed(page, e)) && Boolean(named(page, e, r.cancel)) && Boolean(named(page, e, r.confirm)) &&
    (r.action === "none" || (confirmOf(r.action!) !== undefined && page.some((t) => t.controlType === "ControlType.Text" && t.name === confirmOf(r.action!) && inside(t.rect, e.rect)))),
  "item-group": ({ e, r, page }) =>
    r.showsEmpty === "true"
      ? e.controlType === "ControlType.Text" && Boolean(e.name) && !e.isKeyboardFocusable
      : e.controlType === "ControlType.List" && Boolean(e.name) && !e.isKeyboardFocusable && Boolean(e.rect) &&
        page.filter((x) => x.controlType === "ControlType.ListItem" && inside(x.rect, e.rect)).length === Number(r.items),
  "description-list": ({ e, r, page }) =>
    e.controlType === "ControlType.List" && Number(r.items) > 0 && Boolean(e.rect) &&
    page.filter((x) => x.controlType === "ControlType.ListItem" && /., ./.test(x.name ?? "") && inside(x.rect, e.rect)).length === Number(r.items),
};
/** The tab list of a tab item, found on its page by the id the item reports. */
const tabList = (page: any[], r: Record<string, string>) => page.find((x) => x.automationId === r.list);
/** The colour pairs each component draws, from its self-report: words at 4.5:1, and edges and marks a person must see at 3:1. */
const pairs = (r: Record<string, string>): [string, string, string, number][] => {
  switch (r.kind) {
    case "button":
    case "badge":
      return [["label", r.label!, r.behind!, 4.5]];
    case "text-field":
      return [["label", r.label!, r.behind!, 4.5], ["text", r.text!, r.field!, 4.5], ["description", r.description!, r.behind!, 4.5], ["placeholder", r.placeholder!, r.field!, 4.5], ["error", r.error!, r.behind!, 4.5], ["edge", r.edge!, r.behind!, 3]];
    case "checkbox":
      return [["label", r.label!, r.behind!, 4.5], ["description", r.description!, r.behind!, 4.5], ["edge", r.edge!, r.behind!, 3], ...(r.state === "off" ? [] : [["tick", r.tick!, r.checked!, 3] as [string, string, string, number]])];
    case "select":
      return [["label", r.label!, r.behind!, 4.5], ["text", r.text!, r.field!, 4.5], ["description", r.description!, r.behind!, 4.5], ["placeholder", r.placeholder!, r.field!, 4.5], ["error", r.error!, r.behind!, 4.5], ["edge", r.edge!, r.behind!, 3], ["pressed", r.pressedText!, r.pressedFill!, 3]];
    case "switch":
      return [["label", r.label!, r.behind!, 4.5], ["description", r.description!, r.behind!, 4.5], ["edge", r.edge!, r.behind!, 3], ["thumb", r.thumb!, r.track!, 3]];
    case "separator":
      return r.labelled === "true" ? [["label", r.label!, r.behind!, 4.5]] : [];
    case "tab-bar":
      return [["label", r.label!, r.behind!, 4.5]];
    case "sidebar":
      return [["label", r.label!, r.behind!, 4.5], ["section heading", r.section!, r.plain!, 4.5], ...(r.badge === "true" ? [["badge", r.badgeText!, r.badgeFill!, 4.5] as [string, string, string, number]] : [])];
    case "tabs":
      return [["label", r.label!, r.behind!, 4.5], ...(r.selected === "true" ? [["line", r.line!, r.behind!, 3] as [string, string, string, number]] : [])];
    case "item-group":
      return [r.showsEmpty === "true" ? ["empty message", r.empty!, r.behind!, 4.5] : ["label", r.label!, r.behind!, 4.5]];
    case "description-list":
      return [["term", r.term!, r.behind!, 4.5], ["details", r.details!, r.behind!, 4.5]];
    default:
      return [["title", r.title!, r.surface!, 4.5], ["description", r.description!, r.surface!, 4.5]];
  }
};
/** The components whose pressed state is a fill, so they never scale. */
const NO_SCALE = ["select"];
/** The components whose side-by-side parts stack at large text sizes. */
const STACKS = ["card", "item", "description-list", "dialog", "sheet", "confirm-dialog"];

const checks: Record<string, (kind: string) => Result> = {
  "name.spoken": (kind) => all(of("light", kind).map((x) => ({ ok: spoken[kind]!(x), why: `${key(x)}: ${x.e.controlType} "${x.e.name}" focusable ${x.e.isKeyboardFocusable}` })), `${kind}s named and typed as the spec says`),
  "dismiss.reachable": (kind) => all(of("light", kind).map((x) => { const b = named(x.page, x.e, x.r.close); return { ok: Boolean(b) && b.isKeyboardFocusable, why: `${key(x)}: close "${x.r.close}" ${b ? `found, focusable ${b.isKeyboardFocusable}` : "not found inside it"}` }; }), `${kind}s with a named Close button a screen reader reaches`),
  "target.touch": (kind) => PANELS.includes(kind)
    ? all(of("light", kind).flatMap((x) => { const bs = buttonsIn(x.page, x.e); return bs.length === 0 ? [{ ok: false, why: `${key(x)} has no buttons` }] : bs.map((b) => ({ ok: b.rect.width / x.dpi >= 44 && b.rect.height / x.dpi >= 44, why: `${key(x)} "${b.name}" ${(b.rect.width / x.dpi).toFixed(0)}x${(b.rect.height / x.dpi).toFixed(0)} epx` })); }), `${kind} buttons at least 44x44 epx`)
    : all(of("light", kind).map((x) => !x.e.rect ? { ok: false, why: `${key(x)} not on screen` } : ({ ok: x.e.rect.width / x.dpi >= 44 && x.e.rect.height / x.dpi >= 44, why: `${key(x)} ${(x.e.rect.width / x.dpi).toFixed(0)}x${(x.e.rect.height / x.dpi).toFixed(0)} epx` })), `${kind}s at least 44x44 epx`),
  "motion.reduced": (kind) => {
    const on = of("light", kind), off = of("noanim", kind);
    if (!on.length || !off.length) return none(`${kind}s with animations on and off`);
    // A select's pressed state is a fill and never a scale, so its scale is 1 with animations on too.
    const scales = !NO_SCALE.includes(kind);
    return all([...on.map((x) => ({ ok: scales ? Number(x.r.pressScale) < 1 : Number(x.r.pressScale) === 1, why: `${key(x)} on: ${x.r.pressScale}` })), ...off.map((x) => ({ ok: Number(x.r.pressScale) === 1, why: `${key(x)} off: ${x.r.pressScale}` }))], `${kind}s: press scale 0.97 with animations on, 1 with them off`);
  },
  "text.reflows": (kind) => {
    const small = new Map(of("light", kind).map((x) => [key(x), x]));
    // A panel whose title is hidden has no title text to measure (its words are the content's).
    const big = of("text225", kind).filter((x) => small.has(key(x)) && x.r.accessory !== "true" && x.r.collapsed !== "true" && x.r.hideTitle !== "true");
    return all(big.map((x) => {
      const before = Number(small.get(key(x))!.r.textHeight), after = Number(x.r.textHeight);
      const stacks = STACKS.includes(kind) ? x.r.stacked === "true" : true;
      return { ok: Number(x.r.textScale) >= 2.2 && x.r.trimmed === "false" && after > 1.5 * before && stacks, why: `${key(x)}: text ${before}→${after} px, trimmed ${x.r.trimmed}, stacked ${x.r.stacked ?? "n/a"}` };
    }), `${kind}s at text size 225%: words not cut off, grown${STACKS.includes(kind) ? ", side parts stacked" : ""}`);
  },
  "contrast.text": (kind) => {
    const xs = (["light", "dark", "contrast"] as const).flatMap((s) => of(s, kind).map((x) => ({ ...x, s })));
    return all(xs.flatMap((x) => [
      ...pairs(x.r).map(([what, fg, bg, min]) => ({ ok: ratio(fg, bg) >= min, why: `${x.s} ${key(x)} ${what} ${ratio(fg, bg).toFixed(2)}:1 (needs ${min}:1)` })),
      ...(x.e.sampled && x.r.fill && !x.r.fill.startsWith("#00") ? [{ ok: near(x.e.sampled, x.r.fill), why: `${x.s} ${key(x)} drew ${x.e.sampled}, reports ${x.r.fill}` }] : []),
      ...(x.e.textLine ? [(({ ok, why }) => ({ ok, why: `${x.s} ${key(x)} words: ${why}` }))(wordsAsReported(x.e.textLine, x.r.fill!.startsWith("#00") ? x.r.behind! : x.r.fill!, x.r.label!))] : []),
    ]), `${kind} words at 4.5:1 and edges at 3:1 or better in light, dark and a contrast theme, and sampled fills and words matching the reports`);
  },
};

const rows = specs.flatMap((spec: any) =>
  (spec.claims as string[]).map((id) => {
    const claim = claims.find((c) => c.id === id);
    const check = checks[id];
    const r: Result = check ? check(spec.name) : { ok: false, detail: `core claims it for ${spec.name}, and Windows has no check yet` };
    return { component: spec.name, claim: id, tier: claim?.tier ?? "?", result: r.ok === null ? "not run" : r.ok ? "pass" : "fail", detail: r.detail };
  }),
);
/** One line per spec case, in the shape of the xUnit lines the cross-kit grid already reads: a case passes when every check that applies to it holds and at least one ran. */
const CASED = ["tabs", "tab-bar", "sidebar", "text-field", "checkbox", "switch", "item-group", "select"];
const caseLines = specs.filter((spec: any) => CASED.includes(spec.name)).flatMap((spec: any) =>
  (spec.cases as { id: string }[]).map((c) => {
    scope = `gallery.${spec.name}.${c.id}`;
    const results = (spec.claims as string[]).map((id) => checks[id]?.(spec.name) ?? { ok: false, detail: "no check" });
    scope = undefined;
    const ok = results.every((r) => r.ok !== false) && results.some((r) => r.ok === true);
    return { ok, line: `${ok ? "Passed" : "Failed"} Ouro.Claims.Each_spec_case_holds(name: "${spec.name}/${c.id}")`, why: results.filter((r) => r.ok === false).map((r) => r.detail).join("; ") };
  }),
);
await writeFile(join(dir, "cases.json"), `${JSON.stringify(caseLines, null, 2)}\n`);
const table = ["| Component | Claim | Tier | Windows | Evidence |", "| --- | --- | --- | --- | --- |", ...rows.map((r) => `| ${r.component} | \`${r.claim}\` | ${r.tier} | ${r.result} | ${r.detail.replace(/\|/g, "\\|")} |`)].join("\n");
await writeFile(join(dir, "claims.json"), `${JSON.stringify(rows, null, 2)}\n`);
await writeFile(join(dir, "claims.md"), `## Core's claims on Windows\n\n${table}\n`);
process.stdout.write(`${table}\n`);
process.stdout.write(`${caseLines.map((c) => (c.ok ? c.line : `${c.line}\n  ${c.why}`)).join("\n")}\n`);
process.exit(rows.some((r) => r.result !== "pass") || caseLines.some((c) => !c.ok) ? 1 : 0);
