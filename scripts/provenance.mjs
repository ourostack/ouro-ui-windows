// What comes from ouro-ui-core is vendored from a release, by SHA-256, and never edited (AGENTS.md).
// core-provenance.json names the core tag and, for each file here, the release path it came from and its hash.
//
//   node scripts/provenance.mjs check            every vendored file matches its recorded hash, and every recorded
//                                                hash matches the release's dist-manifest.json (fetched from GitHub)
//   node scripts/provenance.mjs update vX.Y.Z    take every file from that core release and record it
//
// update downloads the release's dist zip with `gh`; check needs only the network.
import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, writeFileSync, mkdirSync, copyFileSync } from "node:fs";
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
  "src/Ouro.UI.Windows/Vendor/OuroTokens.cs": "dist/windows/OuroTokens.cs",
  "src/Ouro.UI.Windows/Vendor/OuroTokens.xaml": "dist/windows/OuroTokens.xaml",
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
  writeFileSync(file, `${JSON.stringify({ coreTag: tag, files }, null, 2)}\n`);
  console.log(`provenance: vendored ${Object.keys(files).length} files from ouro-ui-core ${tag}. Run the generators (README) and commit.`);
} else if (cmd === "check") {
  const recorded = JSON.parse(readFileSync(file, "utf8"));
  const problems = [];
  for (const here of Object.keys(MAP)) if (!recorded.files[here]) problems.push(`${here}: not recorded. Fix: node scripts/provenance.mjs update ${recorded.coreTag}`);
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
    for (const [here, { from, sha256 }] of Object.entries(recorded.files)) if (manifest.files[from] !== sha256) problems.push(`${here}: recorded hash isn't ${recorded.coreTag}'s ${from}. Fix: node scripts/provenance.mjs update ${recorded.coreTag}`);
  }
  for (const p of problems) console.error(`provenance: ${p}`);
  if (problems.length) process.exit(1);
  console.log(`provenance: ${Object.keys(recorded.files).length} files match ouro-ui-core ${recorded.coreTag}.`);
} else {
  console.error("usage: node scripts/provenance.mjs check | update vX.Y.Z");
  process.exit(2);
}
