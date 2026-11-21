// Drives NVDA, through Guidepup, over Ouro Shop's tables page and records what it says (CI job `nvda`).
// The app is already running on the tables page. Each case presses Tab until NVDA says the expected phrase and prints one `NVDA case` line; the log goes to $EV.
import { nvda } from "@guidepup/guidepup";
import { mkdirSync, writeFileSync } from "node:fs";

const ev = process.env.EV ?? "evidence";
mkdirSync(ev, { recursive: true });
const pause = (ms: number) => new Promise((r) => setTimeout(r, ms));
const log: { step: string; spoken: string[] }[] = [];
let seen = 0;

/** Presses a key and returns what NVDA said since the last call. */
async function press(key: string): Promise<string[]> {
  try {
    await nvda.press(key);
  } catch (error) {
    log.push({ step: key, spoken: [`ERROR ${(error as Error).message}`] });
    console.log(`NVDA ${key}: ERROR ${(error as Error).message}`);
    return [];
  }
  await pause(900);
  const all = await nvda.spokenPhraseLog();
  const fresh = all.slice(seen);
  seen = all.length;
  log.push({ step: key, spoken: fresh });
  console.log(`NVDA ${key}: ${JSON.stringify(fresh)}`);
  return fresh;
}

await nvda.start();
try {
  await pause(4000);
  seen = (await nvda.spokenPhraseLog()).length;
  const cases: { name: string; expect: string }[] = [
    { name: "column header", expect: "Serves, header item" },
    { name: "sorted column", expect: "Serves, sorted descending, header item" },
    { name: "row name", expect: "Herb salad, button" },
  ];
  let failed = 0;
  for (const c of cases) {
    let heard: string[] = [];
    for (let i = 0; i < 12 && !heard.includes(c.expect); i++) heard = heard.concat(await press("Tab"));
    const ok = heard.includes(c.expect);
    if (!ok) failed++;
    console.log(`NVDA case ${c.name}: ${ok ? "PASS" : "FAIL"} expected ${JSON.stringify(c.expect)} heard ${JSON.stringify(heard)}`);
  }
  if (failed) process.exitCode = 1;
  writeFileSync(`${ev}/nvda-table.json`, JSON.stringify({ log, all: await nvda.spokenPhraseLog() }, null, 2));
} finally {
  await nvda.stop();
}
