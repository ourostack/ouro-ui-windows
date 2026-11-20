// What comes from ouro-ui-core is vendored from a release, by SHA-256, and never edited (AGENTS.md).
// core-provenance.json names the core tag and, for each file here, the path it came from and its hash. Most come from
// the release's dist/; the composed tree's conformance cases live only in core's source tree, so they come from the
// tag itself (`"source": "tag"`).
//
//   node scripts/provenance.mjs check            every vendored file matches its recorded hash, every recorded hash
//                                                matches the release's dist-manifest.json or the file at the tag, and
//                                                every case at the tag is vendored (all fetched from GitHub)
//   node scripts/provenance.mjs update vX.Y.Z    take every file from that core release and tag, and record it
//
// update downloads with `gh`; check needs only the network (and uses GH_TOKEN, when set, to list the cases).
import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, writeFileSync, mkdirSync, copyFileSync, readdirSync, rmSync, existsSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";

const root = new URL("..", import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1");
const file = join(root, "core-provenance.json");
const sha = (bytes) => createHash("sha256").update(bytes).digest("hex");
const [cmd, tag] = process.argv.slice(2);

/** Where each file here comes from in a core release's dist/. */
const MAP = {
  "vendor/core/ouro-actions.mts": "dist/actions/ouro-actions.mts",
  "vendor/core/ouro-actions-windows.mts": "dist/actions/ouro-actions-windows.mts",
  "vendor/core/components.claims.json": "dist/hx/components.claims.json",
  "vendor/core/components/component.schema.json": "dist/hx/components/component.schema.json",
  "vendor/core/components/button.spec.json": "dist/hx/components/button.spec.json",
  "vendor/core/components/badge.spec.json": "dist/hx/components/badge.spec.json",
  "vendor/core/components/card.spec.json": "dist/hx/components/card.spec.json",
  "vendor/core/components/item.spec.json": "dist/hx/components/item.spec.json",
  "vendor/core/components/checkbox.spec.json": "dist/hx/components/checkbox.spec.json",
  "vendor/core/components/text-field.spec.json": "dist/hx/components/text-field.spec.json",
  "vendor/core/components/separator.spec.json": "dist/hx/components/separator.spec.json",
  "vendor/core/components/description-list.spec.json": "dist/hx/components/description-list.spec.json",
  "vendor/core/components/tabs.spec.json": "dist/hx/components/tabs.spec.json",
  "vendor/core/components/tab-bar.spec.json": "dist/hx/components/tab-bar.spec.json",
  "vendor/core/components/sidebar.spec.json": "dist/hx/components/sidebar.spec.json",
  "vendor/core/components/dialog.spec.json": "dist/hx/components/dialog.spec.json",
  "vendor/core/components/sheet.spec.json": "dist/hx/components/sheet.spec.json",
  "vendor/core/components/confirm-dialog.spec.json": "dist/hx/components/confirm-dialog.spec.json",
  "vendor/core/ouro-composed.mts": "dist/composed/ouro-composed.mts",
  "vendor/core/ouro-mcp-app.mts": "dist/composed/ouro-mcp-app.mts",
  "vendor/core/ouro-a2ui.mts": "dist/a2ui/ouro-a2ui.mts",
  "vendor/core/ouro-a2ui-catalog.mts": "dist/a2ui/ouro-a2ui-catalog.mts",
  "vendor/core/a2ui/catalog.json": "dist/hx/a2ui/catalog.json",
  "src/Ouro.UI.Windows/Vendor/OuroTokens.cs": "dist/windows/OuroTokens.cs",
  "src/Ouro.UI.Windows/Vendor/OuroTokens.xaml": "dist/windows/OuroTokens.xaml",
};

/** Files core keeps in its source tree and no release's dist/: taken from the tag. */
const TREE = {
  "vendor/core/a2ui/conformance/recipe.catalog.json": "standard/hx/a2ui/conformance/recipe.catalog.json",
};
/** Folders of them, every .json file in each: the trees every renderer of the composed tree must draw, and the updates it must apply. */
const TREE_DIRS = {
  "vendor/core/a2ui/conformance/cases": "standard/hx/a2ui/conformance/cases",
  "vendor/core/composed/updates": "standard/hx/composed/updates",
};
const REPO = "ourostack/ouro-ui-core";
const gh = (args) => execFileSync("gh", args, { maxBuffer: 64 * 1024 * 1024 });
const raw = (tag, from) => `https://raw.githubusercontent.com/${REPO}/${tag}/${from}`;
/** The .json files in a folder of core's tree at a tag, by name. */
/** The .json files in a vendored folder here. */
const onDisk = (here) => (existsSync(join(root, here)) ? readdirSync(join(root, here)).filter((n) => n.endsWith(".json")).sort() : []);
const listed = async (tag, dir) => {
  const token = process.env.GH_TOKEN ?? process.env.GITHUB_TOKEN;
  const response = await fetch(`https://api.github.com/repos/${REPO}/contents/${dir}?ref=${tag}`, { headers: { accept: "application/vnd.github+json", ...(token ? { authorization: `Bearer ${token}` } : {}) } });
  if (!response.ok) throw new Error(`listing ${dir} at ${tag}: ${response.status}`);
  return (await response.json()).filter((e) => e.type === "file" && e.name.endsWith(".json")).map((e) => e.name).sort();
};

if (cmd === "update" && /^v\d+\.\d+\.\d+$/.test(tag ?? "")) {
  const dir = mkdtempSync(join(tmpdir(), "ouro-core-"));
  execFileSync("gh", ["release", "download", tag, "-R", "ourostack/ouro-ui-core", "-p", `ouro-ui-core-${tag}-dist.zip`, "-D", dir], { stdio: "inherit" });
  execFileSync("unzip", ["-q", join(dir, `ouro-ui-core-${tag}-dist.zip`), "-d", join(dir, "dist")], { stdio: "inherit" });
  const files = {};
  for (const [here, from] of Object.entries(MAP)) {
    mkdirSync(dirname(join(root, here)), { recursive: true });
    copyFileSync(join(dir, from), join(root, here));
    files[here] = { from, sha256: sha(readFileSync(join(root, here))) };
  }
  const tree = { ...TREE };
  for (const [here, from] of Object.entries(TREE_DIRS)) {
    // A case core dropped or renamed goes too: the folder holds exactly the tag's cases.
    for (const name of onDisk(here)) rmSync(join(root, here, name));
    for (const name of await listed(tag, from)) tree[`${here}/${name}`] = `${from}/${name}`;
  }
  for (const [here, from] of Object.entries(tree)) {
    mkdirSync(dirname(join(root, here)), { recursive: true });
    writeFileSync(join(root, here), gh(["api", "-H", "Accept: application/vnd.github.raw", `repos/${REPO}/contents/${from}?ref=${tag}`]));
    files[here] = { from, source: "tag", sha256: sha(readFileSync(join(root, here))) };
  }
  writeFileSync(file, `${JSON.stringify({ coreTag: tag, files }, null, 2)}\n`);
  console.log(`provenance: vendored ${Object.keys(files).length} files from ouro-ui-core ${tag}. Run the generators (README) and commit.`);
} else if (cmd === "check") {
  const recorded = JSON.parse(readFileSync(file, "utf8"));
  const problems = [];
  // Where each file must come from: a release's dist/, or the tag's tree. A record can't move a file to another path or source.
  const expected = Object.fromEntries([...Object.entries(MAP).map(([h, from]) => [h, { from }]), ...Object.entries(TREE).map(([h, from]) => [h, { from, source: "tag" }])]);
  for (const [here, from] of Object.entries(TREE_DIRS)) {
    try {
      for (const name of await listed(recorded.coreTag, from)) expected[`${here}/${name}`] = { from: `${from}/${name}`, source: "tag" };
    } catch (e) {
      problems.push(`${here}: ${e.message}. Fix: set GH_TOKEN, or try again.`);
    }
    for (const name of onDisk(here)) if (!recorded.files[`${here}/${name}`]) problems.push(`${here}/${name}: not from core. Fix: delete it; vendored folders hold only core's files.`);
  }
  for (const [here, want] of Object.entries(expected)) {
    const got = recorded.files[here];
    if (!got) problems.push(`${here}: not recorded. Fix: node scripts/provenance.mjs update ${recorded.coreTag}`);
    else if (got.from !== want.from || got.source !== want.source) problems.push(`${here}: recorded as ${got.source ?? "dist"} ${got.from}, and comes from ${want.source ?? "dist"} ${want.from}. Fix: node scripts/provenance.mjs update ${recorded.coreTag}`);
  }
  for (const here of Object.keys(recorded.files)) if (!expected[here] && !problems.some((p) => p.startsWith(`${dirname(here)}:`))) problems.push(`${here}: recorded, and nothing here comes from it. Fix: node scripts/provenance.mjs update ${recorded.coreTag}`);
  for (const [here, { from, sha256 }] of Object.entries(recorded.files)) {
    let bytes;
    try { bytes = readFileSync(join(root, here)); } catch { problems.push(`${here}: missing. Fix: node scripts/provenance.mjs update ${recorded.coreTag}`); continue; }
    if (sha(bytes) !== sha256) problems.push(`${here}: differs from ${recorded.coreTag}'s ${from}. Vendored files are never edited. Fix: change core, release it, then update.`);
  }
  const url = `https://github.com/ourostack/ouro-ui-core/releases/download/${recorded.coreTag}/dist-manifest.json`;
  const response = await fetch(url);
  if (!response.ok) problems.push(`${url}: ${response.status}. Fix: is ${recorded.coreTag} released?`);
  else {
    const manifest = await response.json();
    for (const [here, { from, sha256, source }] of Object.entries(recorded.files)) if (source !== "tag" && manifest.files[from] !== sha256) problems.push(`${here}: recorded hash isn't ${recorded.coreTag}'s ${from}. Fix: node scripts/provenance.mjs update ${recorded.coreTag}`);
  }
  for (const [here, { from, sha256, source }] of Object.entries(recorded.files)) {
    if (source !== "tag") continue;
    const at = await fetch(raw(recorded.coreTag, from));
    if (!at.ok) problems.push(`${raw(recorded.coreTag, from)}: ${at.status}. Fix: node scripts/provenance.mjs update ${recorded.coreTag}`);
    else if (sha(Buffer.from(await at.arrayBuffer())) !== sha256) problems.push(`${here}: recorded hash isn't ${from} at ${recorded.coreTag}. Fix: node scripts/provenance.mjs update ${recorded.coreTag}`);
  }
  for (const p of problems) console.error(`provenance: ${p}`);
  if (problems.length) process.exit(1);
  console.log(`provenance: ${Object.keys(recorded.files).length} files match ouro-ui-core ${recorded.coreTag}.`);
} else {
  console.error("usage: node scripts/provenance.mjs check | update vX.Y.Z");
  process.exit(2);
}
