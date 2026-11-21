// Drives NVDA, through Guidepup, over Ouro Shop's tables page and records what it says (CI job `nvda`).
// The app is already running on the tables page. Each key press is one case's evidence; the log goes to $EV.
import { nvda } from "@guidepup/guidepup";
import { mkdirSync, writeFileSync } from "node:fs";

const ev = process.env.EV ?? "evidence";
mkdirSync(ev, { recursive: true });
const pause = (ms: number) => new Promise((r) => setTimeout(r, ms));
const log: { step: string; spoken: string }[] = [];

await nvda.start();
try {
  await pause(4000);
  for (let i = 1; i <= 14; i++) {
    await nvda.press("Tab");
    await pause(700);
    const spoken = await nvda.lastSpokenPhrase();
    log.push({ step: `Tab ${i}`, spoken });
    console.log(`NVDA Tab ${i}: ${spoken}`);
  }
  for (let i = 1; i <= 6; i++) {
    await nvda.press("Control+Alt+ArrowDown");
    await pause(700);
    const spoken = await nvda.lastSpokenPhrase();
    log.push({ step: `table row down ${i}`, spoken });
    console.log(`NVDA row down ${i}: ${spoken}`);
  }
  writeFileSync(`${ev}/nvda-table.json`, JSON.stringify({ log, all: await nvda.spokenPhraseLog() }, null, 2));
} finally {
  await nvda.stop();
}
