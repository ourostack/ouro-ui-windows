// Drives NVDA, through Guidepup, over Ouro Shop's tables page and records what it says (CI job `nvda`).
// The app is already running on the tables page. Each key press is one case's evidence; the log goes to $EV.
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
  for (let i = 1; i <= 8; i++) await press("Tab");
  for (const key of ["Insert+Tab", "Control+Alt+ArrowDown", "Control+Alt+ArrowRight", "Control+Alt+ArrowUp", "ArrowDown", "ArrowUp", "Shift+Tab", "Shift+Tab", "Shift+Tab", "Insert+Tab", "Control+Alt+ArrowRight", "Control+Alt+ArrowDown", "Insert+ArrowUp"]) await press(key);
  writeFileSync(`${ev}/nvda-table.json`, JSON.stringify({ log, all: await nvda.spokenPhraseLog() }, null, 2));
} finally {
  await nvda.stop();
}
