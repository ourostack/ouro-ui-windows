// Core's Button claims, run on Windows. Every claim in core's vendored components.claims.json whose appliesTo names
// "button" must have a Windows check here; a claim core adds fails this until Windows proves it. The checks read UI
// Automation trees of the real window under three Windows settings (defaults, animations off, text size 225%) and
// the token colours the Button draws. Usage: node button-claims.mts <evidence dir> <tokens.json>
import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

const [dir, tokensPath] = process.argv.slice(2);
if (!dir || !tokensPath) {
  process.stderr.write("usage: node button-claims.mts <evidence dir> <tokens.json>\n");
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
const dumps = { base: await json(join(dir, "uia-default.json")), noAnim: await json(join(dir, "uia-noanim.json")), big: await json(join(dir, "uia-text225.json")) };

const buttonsOf = (d: any) => (d?.elements ?? []).filter((e: any) => String(e.automationId).startsWith("action."));
const status = (e: any) => Object.fromEntries(String(e?.itemStatus ?? "").split(";").filter(Boolean).map((kv) => kv.split("=")));
const lum = (hex: string) => {
  const c = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((v) => (v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4));
  return 0.2126 * c[0]! + 0.7152 * c[1]! + 0.0722 * c[2]!;
};
export const ratio = (a: string, b: string) => {
  const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p);
  return (x! + 0.05) / (y! + 0.05);
};

type Result = { ok: boolean | null; detail: string };
const checks: Record<string, () => Result> = {
  "name.spoken": () => {
    const bs = buttonsOf(dumps.base);
    const bad = bs.filter((b: any) => b.controlType !== "ControlType.Button" || !b.name);
    return { ok: dumps.base ? bs.length > 0 && bad.length === 0 : null, detail: bs.map((b: any) => `${b.automationId}: ${b.controlType.replace("ControlType.", "")} "${b.name}"`).join("; ") };
  },
  "target.touch": () => {
    const scale = (dumps.base?.systemDpi ?? 96) / 96;
    const bs = buttonsOf(dumps.base);
    const sizes = bs.map((b: any) => [b.rect.width / scale, b.rect.height / scale]);
    return { ok: dumps.base ? bs.length > 0 && sizes.every(([w, h]: number[]) => w! >= 44 && h! >= 44) : null, detail: sizes.map(([w, h]: number[]) => `${w!.toFixed(0)}x${h!.toFixed(0)} epx`).join(", ") };
  },
  "motion.reduced": () => {
    const on = buttonsOf(dumps.base).map((b: any) => status(b).pressScale);
    const off = buttonsOf(dumps.noAnim).map((b: any) => status(b).pressScale);
    return { ok: dumps.base && dumps.noAnim ? on.every((s: string) => Number(s) < 1) && off.length > 0 && off.every((s: string) => Number(s) === 1) : null, detail: `animations on: press scale ${on.join(", ")}; off: ${off.join(", ")}` };
  },
  "text.reflows": () => {
    const small = buttonsOf(dumps.base).map(status);
    const big = buttonsOf(dumps.big).map(status);
    const grew = big.every((s: any, i: number) => Number(s.labelHeight) > 1.5 * Number(small[i]?.labelHeight ?? Infinity));
    return { ok: dumps.base && dumps.big ? big.length > 0 && big.every((s: any) => s.trimmed === "false" && Number(s.textScale) >= 2.2) && grew : null, detail: `text scale ${big.map((s: any) => s.textScale).join(", ")}; label height ${small.map((s: any) => s.labelHeight).join(", ")} → ${big.map((s: any) => s.labelHeight).join(", ")}; trimmed ${big.map((s: any) => s.trimmed).join(", ")}` };
  },
  "contrast.text": () => {
    if (!tokens) return { ok: null, detail: "no tokens" };
    const pairs: [string, string, string][] = [];
    for (const mode of ["light", "dark"]) for (const v of ["primary", "secondary"]) pairs.push([`${mode} ${v}`, tokens[mode][`${v}Foreground`], tokens[mode][v]]);
    const rs = pairs.map(([n, f, b]) => [n, ratio(f, b)] as const);
    return { ok: rs.every(([, r]) => r >= 4.5), detail: `${rs.map(([n, r]) => `${n} ${r.toFixed(2)}:1`).join(", ")}; contrast themes draw the system's own colours (the Button keeps no colours of its own under them)` };
  },
};

const rows = claims.filter((c) => c.appliesTo.includes("button")).map((c) => {
  const check = checks[c.id];
  const r: Result = check ? check() : { ok: false, detail: "core claims it for Button, and Windows has no check yet" };
  return { claim: c.id, tier: c.tier, result: r.ok === null ? "not run" : r.ok ? "pass" : "fail", detail: r.detail };
});
const table = ["| Claim | Tier | Windows | Evidence |", "| --- | --- | --- | --- |", ...rows.map((r) => `| \`${r.claim}\` | ${r.tier} | ${r.result} | ${r.detail.replace(/\|/g, "\\|")} |`)].join("\n");
await writeFile(join(dir, "button-claims.json"), `${JSON.stringify(rows, null, 2)}\n`);
await writeFile(join(dir, "button-claims.md"), `## Core's Button claims on Windows\n\n${table}\n`);
process.stdout.write(`${table}\n`);
process.exit(rows.some((r) => r.result !== "pass") ? 1 : 0);
