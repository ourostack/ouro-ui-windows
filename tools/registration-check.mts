import { readFileSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

type Extension = { app?: string; category?: string; name?: string; alias?: string; protocol?: string };

export type Registration = { ok: boolean; problems: string[]; actions: string[]; tools: string[]; doors: string[] };

const read = (path: string) => JSON.parse(readFileSync(path, "utf8").replace(/^\uFEFF/, ""));

// What Windows itself recorded for the installed package (Get-AppxPackageManifest), set against what was generated.
export function check(installed: { extensions: Extension[] }, registeredActions: { actions: { id: string }[] }, generatedActions: { actions: { id: string }[] }, registeredMcp: { tools: { name: string }[] }, generatedMcp: { tools: { name: string }[] }): Registration {
  const problems: string[] = [];
  const doors: string[] = [];
  const has = (app: string, category: string, field: keyof Extension, value?: string) => installed.extensions.some((e) => e.app === app && e.category === category && (value === undefined ? Boolean(e[field]) : e[field] === value));
  if (has("App", "windows.appExtension", "name", "com.microsoft.windows.ai.actions")) doors.push("app-actions");
  else problems.push("Windows did not register the App Actions extension (com.microsoft.windows.ai.actions) on the app");
  if (has("Mcp", "windows.appExtension", "name", "com.microsoft.windows.ai.mcpServer")) doors.push("mcp");
  else problems.push("Windows did not register the MCP server extension (com.microsoft.windows.ai.mcpServer) on the Mcp application");
  if (!has("App", "windows.protocol", "protocol")) problems.push("Windows did not register the app's URI scheme, which App Actions launch through");
  if (!has("Mcp", "windows.appExecutionAlias", "alias")) problems.push("Windows did not register the MCP server's execution alias, which the Agent Registry starts it by");
  const same = (label: string, installedIds: string[], generatedIds: string[]) => {
    if (generatedIds.length === 0) problems.push(`the generated ${label} list is empty, so there is nothing to compare`);
    if (JSON.stringify([...installedIds].sort()) !== JSON.stringify([...generatedIds].sort())) problems.push(`the installed package's ${label} are [${installedIds}], and the declarations generate [${generatedIds}]`);
  };
  const actions = registeredActions.actions.map((a) => a.id);
  const tools = registeredMcp.tools.map((t) => t.name);
  same("App Actions", actions, generatedActions.actions.map((a) => a.id));
  same("MCP tools", tools, generatedMcp.tools.map((t) => t.name));
  return { ok: problems.length === 0, problems, actions, tools, doors };
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const [pkg, registeredActions, generatedActions, registeredMcp, generatedMcp, out] = process.argv.slice(2);
  const result = check(read(pkg), read(registeredActions), read(generatedActions), read(registeredMcp), read(generatedMcp));
  writeFileSync(out, JSON.stringify(result, null, 2));
  console.log(JSON.stringify(result, null, 2));
  if (!result.ok) {
    for (const problem of result.problems) console.log(`::error::${problem}`);
    process.exit(1);
  }
}
