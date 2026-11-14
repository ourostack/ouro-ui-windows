// Core's Badge claims, run on Windows, the way button-claims.mts runs the Button's. Every claim in core's vendored
// components.claims.json whose appliesTo names "badge" must have a Windows check here. Core's claims 0.2.0 (ouro-ui-core
// #48, on main and not yet in a release) adds the Badge to the three claims about words; until the vendored pin moves
// to a release that carries them, `ahead` holds them so Windows proves them now. The checks read the same UI
// Automation trees as the Button's (defaults, animations off, text size 225%) and the token colours.
// Usage: node badge-claims.mts <evidence dir> <tokens.json>
import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

const ahead = ["name.spoken", "text.reflows", "contrast.text"];

const [dir, tokensPath] = process.argv.slice(2);
if (!dir || !tokensPath) {
  process.stderr.write("usage: node badge-claims.mts <evidence dir> <tokens.json>\n");
  process.exit(2);
}
const json = async (p: string) => {
  try {
    return JSON.parse((await readFile(p, "utf8")).replace(/^﻿/, ""));
  } catch {
    return null;
  }
};
const claims = (await json(new URL("./vendor/components.claims.json", import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1")))?.claims as { id: string; tier: string; appliesTo: string[] }[];
const tokens = await json(tokensPath);
const dumps = { base: await json(join(dir, "uia-default.json")), big: await json(join(dir, "uia-text225.json")) };

const badgesOf = (d: any) => (d?.elements ?? []).filter((e: any) => String(e.automationId).startsWith("badge."));
const status = (e: any) => Object.fromEntries(String(e?.itemStatus ?? "").split(";").filter(Boolean).map((kv) => kv.split("=")));
const lum = (hex: string) => {
  const c = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((v) => (v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4));
  return 0.2126 * c[0]! + 0.7152 * c[1]! + 0.0722 * c[2]!;
};
const ratio = (a: string, b: string) => {
  const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p);
  return (x! + 0.05) / (y! + 0.05);
};

type Result = { ok: boolean | null; detail: string };
const checks: Record<string, () => Result> = {
  "name.spoken": () => {
    const bs = badgesOf(dumps.base);
    const bad = bs.filter((b: any) => b.controlType !== "ControlType.Text" || !b.name || b.isKeyboardFocusable);
    const icons = (dumps.base?.elements ?? []).filter((e: any) => e.className === "FontIcon");
    return {
      ok: dumps.base ? bs.length > 0 && bad.length === 0 && icons.length === 0 : null,
      detail: `${bs.map((b: any) => `${b.automationId}: ${b.controlType.replace("ControlType.", "")} "${b.name}", focusable ${b.isKeyboardFocusable}`).join("; ")}; decorative icons in the tree: ${icons.length}`,
    };
  },
  "text.reflows": () => {
    const small = badgesOf(dumps.base).map(status);
    const big = badgesOf(dumps.big).map(status);
    const grew = big.every((s: any, i: number) => Number(s.labelHeight) > 1.5 * Number(small[i]?.labelHeight ?? Infinity));
    return { ok: dumps.base && dumps.big ? big.length > 0 && big.every((s: any) => s.trimmed === "false" && Number(s.textScale) >= 2.2) && grew : null, detail: `text scale ${big.map((s: any) => s.textScale).join(", ")}; label height ${small.map((s: any) => s.labelHeight).join(", ")} → ${big.map((s: any) => s.labelHeight).join(", ")}; trimmed ${big.map((s: any) => s.trimmed).join(", ")}` };
  },
  "contrast.text": () => {
    if (!tokens) return { ok: null, detail: "no tokens" };
    const pairs: [string, string, string][] = [];
    for (const mode of ["light", "dark"]) for (const v of ["primary", "secondary"]) pairs.push([`${mode} ${v}`, tokens[mode][`${v}Foreground`], tokens[mode][v]]);
    const rs = pairs.map(([n, f, b]) => [n, ratio(f, b)] as const);
    // The window draws the token pair it was checked against, not colours of its own.
    const drawn = badgesOf(dumps.base).map(status);
    const matches = drawn.map((s: any) => ["light", "dark"].some((m) => tokens[m][s.tone] === s.fill && tokens[m][`${s.tone}Foreground`] === s.label));
    return {
      ok: dumps.base ? rs.every(([, r]) => r >= 4.5) && drawn.length > 0 && matches.every(Boolean) : null,
      detail: `${rs.map(([n, r]) => `${n} ${r.toFixed(2)}:1`).join(", ")}; drawn ${drawn.map((s: any) => `${s.tone} ${s.label} on ${s.fill}`).join(", ")}; contrast themes draw the system's own colours`,
    };
  },
};

const rows = claims
  .filter((c) => c.appliesTo.includes("badge") || ahead.includes(c.id))
  .map((c) => {
    const check = checks[c.id];
    const r: Result = check ? check() : { ok: false, detail: "core claims it for Badge, and Windows has no check yet" };
    return { claim: c.id, tier: c.tier, source: c.appliesTo.includes("badge") ? "vendored" : "core main (0.2.0)", result: r.ok === null ? "not run" : r.ok ? "pass" : "fail", detail: r.detail };
  });
const table = ["| Claim | Tier | From | Windows | Evidence |", "| --- | --- | --- | --- | --- |", ...rows.map((r) => `| \`${r.claim}\` | ${r.tier} | ${r.source} | ${r.result} | ${r.detail.replace(/\|/g, "\\|")} |`)].join("\n");
await writeFile(join(dir, "badge-claims.json"), `${JSON.stringify(rows, null, 2)}\n`);
await writeFile(join(dir, "badge-claims.md"), `## Core's Badge claims on Windows\n\n${table}\n`);
process.stdout.write(`${table}\n`);
process.exit(rows.length === 0 || rows.some((r) => r.result !== "pass") ? 1 : 0);
