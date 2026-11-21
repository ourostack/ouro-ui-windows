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
  const nav = ["Insert+Numpad5", "Insert+Numpad8", "Insert+Numpad5", "Insert+Numpad2", "Insert+Numpad5", "Insert+Numpad6", "Insert+Numpad5", "Insert+Numpad6", "Insert+Numpad5", "Insert+Numpad6", "Insert+Numpad5", "Insert+Numpad8", "Insert+Numpad8", "Insert+Numpad5", "Insert+Numpad4", "Insert+Numpad5", "Insert+Numpad2", "Insert+Numpad5"];
  for (const target of ["Serves, sorted descending", "Herb salad, button"]) {
    for (let i = 0; i < 12; i++) {
      const said = await press("Tab");
      await pause(600);
      if (said.concat((await nvda.spokenPhraseLog()).slice(-2)).some((p) => p.startsWith(target))) break;
    }
    console.log(`NVDA --- at ${target}`);
    for (const key of nav) await press(key);
  }
  writeFileSync(`${ev}/nvda-table.json`, JSON.stringify({ log, all: await nvda.spokenPhraseLog() }, null, 2));
} finally {
  await nvda.stop();
}
