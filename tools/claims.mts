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
const vendor = new URL("../vendor/core/", import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1");
const claims = (await json(join(vendor, "components.claims.json")))?.claims as { id: string; tier: string; appliesTo: string[] }[];
/** The components this kit ships. Core specs more; each one joins here when its control does, with a check for each claim. */
const KIT = ["button", "badge", "card", "item"];
const specs = (await Promise.all((await readdir(join(vendor, "components"))).filter((f) => f.endsWith(".spec.json")).map((f) => json(join(vendor, "components", f))))).filter((s: any) => KIT.includes(s?.name));
const missing = KIT.filter((k) => !specs.some((s: any) => s.name === k));
if (missing.length) {
  process.stderr.write(`claims: no vendored spec for ${missing.join(", ")}. Fix: node scripts/provenance.mjs update <core tag>.\n`);
  process.exit(1);
}

/** Every dump, by the setting it was taken under. Each holds the list page and the gallery page. */
const settings = ["light", "dark", "contrast", "noanim", "text225"] as const;
type Setting = (typeof settings)[number];
const dumps = Object.fromEntries(await Promise.all(settings.map(async (s) => [s, [await json(join(dir, `uia-${s}.json`)), await json(join(dir, `uia-gallery-${s}.json`))].filter(Boolean)] as const))) as Record<Setting, any[]>;

const report = (e: any): Record<string, string> => {
  const m = /ouro:(\w+);(.*)$/.exec(String(e?.itemStatus ?? ""));
  return m ? { kind: m[1]!, ...Object.fromEntries(m[2]!.split(";").filter(Boolean).map((kv) => kv.split("=") as [string, string])) } : {};
};
/** The kit's controls of one kind under a setting, each with its self-report, keyed by automation id or name. */
const of = (setting: Setting, kind: string) =>
  dumps[setting].flatMap((d) => (d.elements as any[]).map((e) => ({ e, r: report(e), dpi: (d.systemDpi ?? 96) / 96 })).filter((x) => x.r.kind === kind));
const key = (x: { e: any }) => x.e.automationId || x.e.name;

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

/** What UI Automation must see for each component (its spec's accessibility.windows). */
const spoken: Record<string, (x: { e: any; r: Record<string, string> }) => boolean> = {
  button: ({ e }) => e.controlType === "ControlType.Button" && Boolean(e.name) && e.isKeyboardFocusable,
  badge: ({ e }) => e.controlType === "ControlType.Text" && Boolean(e.name) && !e.isKeyboardFocusable,
  card: ({ e }) => e.controlType === "ControlType.Group" && Boolean(e.name),
  item: ({ e, r }) => (r.pressable === "true" ? e.controlType === "ControlType.Button" && e.isKeyboardFocusable && Boolean(e.helpText) : e.controlType === "ControlType.Group" && !e.isKeyboardFocusable) && Boolean(e.name),
};
/** The colour pairs each component's words sit on, from its self-report. */
const pairs = (r: Record<string, string>): [string, string, string][] =>
  r.kind === "button" || r.kind === "badge" ? [["label", r.label!, r.behind!]] : [["title", r.title!, r.surface!], ["description", r.description!, r.surface!]];

const checks: Record<string, (kind: string) => Result> = {
  "name.spoken": (kind) => all(of("light", kind).map((x) => ({ ok: spoken[kind]!(x), why: `${key(x)}: ${x.e.controlType} "${x.e.name}" focusable ${x.e.isKeyboardFocusable}` })), `${kind}s named and typed as the spec says`),
  "target.touch": (kind) => all(of("light", kind).map((x) => !x.e.rect ? { ok: false, why: `${key(x)} not on screen` } : ({ ok: x.e.rect.width / x.dpi >= 44 && x.e.rect.height / x.dpi >= 44, why: `${key(x)} ${(x.e.rect.width / x.dpi).toFixed(0)}x${(x.e.rect.height / x.dpi).toFixed(0)} epx` })), `${kind}s at least 44x44 epx`),
  "motion.reduced": (kind) => {
    const on = of("light", kind), off = of("noanim", kind);
    if (!on.length || !off.length) return none(`${kind}s with animations on and off`);
    return all([...on.map((x) => ({ ok: Number(x.r.pressScale) < 1, why: `${key(x)} on: ${x.r.pressScale}` })), ...off.map((x) => ({ ok: Number(x.r.pressScale) === 1, why: `${key(x)} off: ${x.r.pressScale}` }))], `${kind}s: press scale 0.97 with animations on, 1 with them off`);
  },
  "text.reflows": (kind) => {
    const small = new Map(of("light", kind).map((x) => [key(x), x]));
    const big = of("text225", kind).filter((x) => small.has(key(x)));
    return all(big.map((x) => {
      const before = Number(small.get(key(x))!.r.textHeight), after = Number(x.r.textHeight);
      const stacks = kind === "card" || kind === "item" ? x.r.stacked === "true" : true;
      return { ok: Number(x.r.textScale) >= 2.2 && x.r.trimmed === "false" && after > 1.5 * before && stacks, why: `${key(x)}: text ${before}→${after} px, trimmed ${x.r.trimmed}, stacked ${x.r.stacked ?? "n/a"}` };
    }), `${kind}s at text size 225%: words not cut off, grown${kind === "card" || kind === "item" ? ", side parts stacked" : ""}`);
  },
  "contrast.text": (kind) => {
    const xs = (["light", "dark", "contrast"] as const).flatMap((s) => of(s, kind).map((x) => ({ ...x, s })));
    return all(xs.flatMap((x) => [
      ...pairs(x.r).map(([what, fg, bg]) => ({ ok: ratio(fg, bg) >= 4.5, why: `${x.s} ${key(x)} ${what} ${ratio(fg, bg).toFixed(2)}:1` })),
      ...(x.e.sampled && x.r.fill && !x.r.fill.startsWith("#00") ? [{ ok: near(x.e.sampled, x.r.fill), why: `${x.s} ${key(x)} drew ${x.e.sampled}, reports ${x.r.fill}` }] : []),
      ...(x.e.textLine ? [(({ ok, why }) => ({ ok, why: `${x.s} ${key(x)} words: ${why}` }))(wordsAsReported(x.e.textLine, x.r.fill!.startsWith("#00") ? x.r.behind! : x.r.fill!, x.r.label!))] : []),
    ]), `${kind} text pairs at 4.5:1 or better in light, dark and a contrast theme, and sampled fills and words matching the reports`);
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
const table = ["| Component | Claim | Tier | Windows | Evidence |", "| --- | --- | --- | --- | --- |", ...rows.map((r) => `| ${r.component} | \`${r.claim}\` | ${r.tier} | ${r.result} | ${r.detail.replace(/\|/g, "\\|")} |`)].join("\n");
await writeFile(join(dir, "claims.json"), `${JSON.stringify(rows, null, 2)}\n`);
await writeFile(join(dir, "claims.md"), `## Core's claims on Windows\n\n${table}\n`);
process.stdout.write(`${table}\n`);
process.exit(rows.some((r) => r.result !== "pass") ? 1 : 0);
