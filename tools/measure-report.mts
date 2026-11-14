// Puts the measurement files side by side as one Markdown table. Usage: node measure-report.mts <out.md> <measure.json>...
import { readFile, writeFile } from "node:fs/promises";
const [out, ...ins] = process.argv.slice(2);
const docs = await Promise.all(ins.map(async (p) => JSON.parse((await readFile(p, "utf8")).replace(/^﻿/, "")).summary));
const cols: [string, string][] = [["coldReadyMs", "Cold launch to button (ms)"], ["warmWindowMsMedian", "Warm launch to window, median (ms)"], ["warmReadyMsMedian", "Warm launch to button, median (ms)"], ["workingSetMBMedian", "Working set, median (MB)"], ["privateMBMedian", "Private bytes, median (MB)"], ["pressToStatusMsMedian", "Press to status, median (ms)"], ["pressToStatusMsP95", "Press to status, p95 (ms)"]];
const table = [`| Measure | ${docs.map((d) => d.label).join(" | ")} |`, `| --- | ${docs.map(() => "---").join(" | ")} |`, ...cols.map(([k, n]) => `| ${n} | ${docs.map((d) => d[k]).join(" | ")} |`)].join("\n");
const md = `## Feel on windows-latest\n\n${table}\n\nRunner: ${docs[0]?.os}, ${docs[0]?.cpu}, ${docs[0]?.logicalProcessors} logical processors. Launch time is measured to the first action button in the UI Automation tree; press latency is a UI Automation Invoke to the status text changing.\n`;
await writeFile(out, md);
process.stdout.write(md);
