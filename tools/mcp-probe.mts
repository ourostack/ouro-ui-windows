// Talks to an MCP server over stdio the way an agent does: initialize, tools/list, then a write, a consequential call
// (the server asks the person; CI answers its dialog with tools/answer-dialog.ps1), a person-only call, and a retry with
// the same idempotency key. Writes what it heard as JSON for the X-ray. Usage: node mcp-probe.mts <out.json> <command> [args...]
import { spawn } from "node:child_process";
import { writeFile } from "node:fs/promises";
import { createInterface } from "node:readline";

const [out, command, ...args] = process.argv.slice(2);
if (!out || !command) {
  process.stderr.write("usage: node mcp-probe.mts <out.json> <command> [args...]\n");
  process.exit(2);
}
const started = performance.now();
const child = spawn(command, args, { stdio: ["pipe", "pipe", "inherit"] });
const lines = createInterface({ input: child.stdout! });
const waiting = new Map<number, (v: unknown) => void>();
lines.on("line", (line) => {
  const msg = JSON.parse(line) as { id?: number };
  if (typeof msg.id === "number") waiting.get(msg.id)?.(msg);
});
let next = 1;
const call = (method: string, params: unknown) =>
  new Promise<any>((resolveCall, reject) => {
    const id = next++;
    const timer = setTimeout(() => reject(new Error(`${method}: no answer in 60 s`)), 60000);
    waiting.set(id, (v) => {
      clearTimeout(timer);
      resolveCall(v);
    });
    child.stdin!.write(`${JSON.stringify({ jsonrpc: "2.0", id, method, params })}\n`);
  });
const init = await call("initialize", { protocolVersion: "2025-06-18", capabilities: {}, clientInfo: { name: "ouro-mcp-probe", version: "0.1.0" } });
const firstAnswerMs = Math.round(performance.now() - started);
child.stdin!.write(`${JSON.stringify({ jsonrpc: "2.0", method: "notifications/initialized" })}\n`);
const list = await call("tools/list", {});
const add = await call("tools/call", { name: "add_shopping_list_item", arguments: { name: "oat milk" }, _meta: { "ouro/idempotencyKey": "probe-1" } });
const retry = await call("tools/call", { name: "add_shopping_list_item", arguments: { name: "oat milk" }, _meta: { "ouro/idempotencyKey": "probe-1" } });
const share = await call("tools/call", { name: "share_shopping_list", arguments: { with: "sam@example.com" } });
const order = await call("tools/call", { name: "place_grocery_order", arguments: {} });
child.stdin!.end();
child.kill();
await writeFile(out, `${JSON.stringify({ command, firstAnswerMs, initialize: init.result, toolsList: list.result, calls: { add_shopping_list_item: add.result, add_shopping_list_item_retry: retry.result, share_shopping_list: share.result, place_grocery_order: order.result } }, null, 2)}\n`);
process.stdout.write(`mcp-probe: ${list.result.tools.length} tools, first answer in ${firstAnswerMs} ms, wrote ${out}\n`);
process.exit(0);
