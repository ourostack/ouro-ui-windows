// Spike: the Windows projections of ouro-ui-core's one action declaration. It writes core's own files (mcp.json and
// friends, through the vendored generator) and three Windows ones:
//   app-actions.json   Windows App Actions registration (schema version 3), for the package's
//                      com.microsoft.windows.ai.actions app extension. Person-only actions are never in it.
//   mcpb.manifest.json the MCP bundle manifest that registers the MCP server in the On-device Agent Registry, with
//                      _meta static responses equal to core's mcp.json, so ODR can list the tools without running it.
//   OuroActions.g.cs   C# for the app, its App Action handler and its MCP server: each action's facts, the typed
//                      refusal, the tools list and one guarded entry point that every door goes through.
// If the spike holds, this moves into ouro-ui-core as a projection beside OuroActions.swift.
import { mkdir, readFile, readdir, writeFile } from "node:fs/promises";
import { join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { refusal, renderActions, toMcpTool, type Action, type ActionSet, type Field } from "./vendor/ouro-actions.mts";

export const WINDOWS_GENERATOR = "ouro-actions-windows 0.1.0-spike";

export interface WindowsOptions {
  /** The URI scheme the packaged app registers (uap:Protocol); App Actions launch it. */
  scheme: string;
  /** The MCP server's executable inside the package, and its display name. */
  mcpExe: string;
  serverName: string;
  version: string;
}

/** Why an action has no App Action, or null when it has one. App Actions take only Text entities for typed strings. */
export function appActionGap(a: Action): string | null {
  if (a.consent === "person-only") return "person-only: no agent door offers it";
  for (const [name, f] of Object.entries(a.input)) {
    if (f.protocolOnly) continue;
    if (f.kind !== "string" && !f.optional) return `required input "${name}" is a ${f.kind}, and App Actions entities carry text, files, photos, tables and contacts only`;
  }
  return null;
}

const textInputs = (a: Action) => Object.entries(a.input).filter(([, f]) => !f.protocolOnly && f.kind === "string") as [string, Field][];

export function toAppAction(a: Action, o: WindowsOptions) {
  const inputs = textInputs(a);
  const required = inputs.filter(([, f]) => !f.optional).map(([n]) => n);
  const all = inputs.map(([n]) => n);
  const phrase = (names: string[]) => (names.length ? `${a.label}: ${names.map((n) => `\${${n}.Text}`).join(", ")}` : a.label);
  const combos = [{ inputs: required, description: phrase(required) }];
  if (all.length > required.length) combos.push({ inputs: all, description: phrase(all) });
  const query = [...all.map((n) => `${n}=\${${n}.Text}`), "token=${$.Token}"].join("&");
  return {
    id: `Ouro.${a.names.pascal}`,
    description: a.description,
    displaysUI: true,
    usesGenerativeAI: false,
    isAvailable: true,
    allowedAppInvokers: ["*"],
    inputs: inputs.map(([name, f]) => (f.optional ? { name, kind: "Text", required: false } : { name, kind: "Text" })),
    inputCombinations: combos,
    invocation: { type: "uri", uri: `${o.scheme}://action/${a.id}?${query}` },
  };
}

const json = (v: unknown) => `${JSON.stringify(v, null, 2)}\n`;
const agentDoors = (set: ActionSet) => set.actions.filter((a) => a.consent !== "person-only");

export function renderAppActions(set: ActionSet, o: WindowsOptions) {
  const actions = set.actions.filter((a) => appActionGap(a) === null).map((a) => toAppAction(a, o));
  const skipped = Object.fromEntries(set.actions.filter((a) => appActionGap(a) !== null).map((a) => [a.id, appActionGap(a)!]));
  return { registration: { version: 3, actions }, skipped };
}

export function renderMcpb(set: ActionSet, o: WindowsOptions) {
  const tools = agentDoors(set).map(toMcpTool);
  return {
    manifest_version: "0.1",
    name: o.serverName,
    version: o.version,
    description: `Ouro UI actions for ${set.name}, generated from their declarations.`,
    author: { name: "Ouro" },
    server: { type: "binary", entry_point: o.mcpExe, mcp_config: { command: o.mcpExe, args: [] } },
    tools: tools.map((t) => ({ name: t.name, description: t.description })),
    tools_generated: false,
    license: "MIT",
    _meta: {
      "com.microsoft.windows": {
        static_responses: {
          initialize: { protocolVersion: "2025-06-18", capabilities: { tools: { listChanged: false } }, serverInfo: { name: o.serverName, version: o.version } },
          "tools/list": { tools },
        },
      },
    },
  };
}

const cs = (s: string) => JSON.stringify(s);
const raw = (s: string) => {
  if (s.includes('"""')) throw new Error("a generated JSON string holds three quotes in a row, which a C# raw string can't");
  return `"""\n${s}\n"""`;
};
const consentCase = (a: Action) => ({ read: "Read", write: "Write", consequential: "Consequential", "person-only": "PersonOnly" })[a.consent];

export function renderCSharp(set: ActionSet, o: WindowsOptions): string {
  const L: string[] = [];
  L.push(`// <auto-generated> ${WINDOWS_GENERATOR} from the "${set.name}" action set. Don't edit; change the declarations and generate again.`);
  L.push("#nullable enable");
  L.push("using System.Collections.Generic;");
  L.push("using System.Linq;");
  L.push("using System.Text.Json.Nodes;");
  L.push("using System.Threading.Tasks;");
  L.push("");
  L.push("namespace Ouro.Actions.Generated;");
  L.push("");
  L.push("public enum OuroConsent { Read, Write, Consequential, PersonOnly }");
  L.push("");
  L.push("/// <summary>Which door a call came through. Only the person's own press may start a person-only action.</summary>");
  L.push("public enum OuroDoor { Person, Mcp, AppAction }");
  L.push("");
  L.push("public sealed record OuroActionInfo(string Id, OuroConsent Consent, string Label, string Description, string Agent)");
  L.push("{");
  L.push("    /// <summary>The UI Automation id of the control a person presses for it, which the X-ray reads.</summary>");
  L.push('    public string AutomationId => "action." + Id;');
  L.push("    public bool AgentsMayCall => Consent != OuroConsent.PersonOnly;");
  L.push("}");
  L.push("");
  L.push("public static class OuroActions");
  L.push("{");
  L.push(`    public const string Set = ${cs(set.name)};`);
  L.push(`    public const string Scheme = ${cs(o.scheme)};`);
  for (const a of set.actions) L.push(`    public static readonly OuroActionInfo ${a.names.pascal} = new(${cs(a.id)}, OuroConsent.${consentCase(a)}, ${cs(a.label)}, ${cs(a.description)}, ${cs(a.agent)});`);
  L.push(`    public static readonly IReadOnlyList<OuroActionInfo> All = new[] { ${set.actions.map((a) => a.names.pascal).join(", ")} };`);
  L.push("    public static OuroActionInfo? Find(string id) => All.FirstOrDefault(a => a.Id == id);");
  L.push("");
  L.push("    /// <summary>What every agent door answers a person-only action: core's typed refusal, byte for byte.</summary>");
  L.push("    public static readonly IReadOnlyDictionary<string, string> Refusals = new Dictionary<string, string>");
  L.push("    {");
  for (const a of set.actions.filter((x) => x.consent === "person-only")) L.push(`        [${cs(a.id)}] = ${raw(JSON.stringify(refusal(a)))},`);
  L.push("    };");
  L.push("");
  L.push("    /// <summary>The MCP tools, as core's mcp.json lists them: what tools/list answers.</summary>");
  L.push(`    public const string McpToolsJson = ${raw(JSON.stringify(agentDoors(set).map(toMcpTool)))};`);
  L.push("");
  L.push("    /// <summary>The app's one handler for every action; the button, the App Action and the MCP server all reach it.</summary>");
  L.push("    public static System.Func<string, JsonObject, Task<JsonObject>>? Handler { get; set; }");
  L.push("");
  L.push("    /// <summary>Runs an action through a door: an agent door gets the refusal for a person-only action and never reaches the handler.</summary>");
  L.push("    public static async Task<JsonNode> InvokeAsync(string id, JsonObject input, OuroDoor door)");
  L.push("    {");
  L.push('        var action = Find(id) ?? throw new KeyNotFoundException($"no action \\"{id}\\" is declared");');
  L.push("        if (door != OuroDoor.Person && !action.AgentsMayCall) return JsonNode.Parse(Refusals[id])!;");
  L.push('        var handler = Handler ?? throw new System.InvalidOperationException($"{id}: no handler is bound. Set OuroActions.Handler at launch.");');
  L.push("        return await handler(id, input);");
  L.push("    }");
  L.push("}");
  return `${L.join("\n")}\n`;
}

export function renderWindows(set: ActionSet, o: WindowsOptions): Map<string, string> {
  const files = new Map<string, string>();
  for (const [name, text] of renderActions(set)) if (name.endsWith(".json")) files.set(`core/${name}`, text);
  const { registration, skipped } = renderAppActions(set, o);
  files.set("windows/app-actions.json", json(registration));
  files.set("windows/app-actions.skipped.json", json({ generator: WINDOWS_GENERATOR, set: set.name, skipped }));
  files.set("windows/mcpb.manifest.json", json(renderMcpb(set, o)));
  files.set("windows/OuroActions.g.cs", renderCSharp(set, o));
  return files;
}

async function readTree(dir: string, prefix = ""): Promise<Map<string, string>> {
  const found = new Map<string, string>();
  let entries;
  try {
    entries = await readdir(join(dir, prefix), { withFileTypes: true });
  } catch {
    return found;
  }
  for (const e of entries) {
    const rel = prefix ? `${prefix}/${e.name}` : e.name;
    if (e.isDirectory()) for (const [k, v] of await readTree(dir, rel)) found.set(k, v);
    else found.set(rel, await readFile(join(dir, rel), "utf8"));
  }
  return found;
}

export const SPIKE_OPTIONS: WindowsOptions = { scheme: "ouro-shop-spike", mcpExe: "OuroShop.Mcp.exe", serverName: "ouro-shop-spike", version: "0.1.0" };

/** generate|check <actions.mts> <dir>: 0 clean, 1 drift, 2 usage. */
export async function main(argv: readonly string[]): Promise<number> {
  const [cmd, decl, dir] = argv;
  if (!cmd || !decl || !dir || !["generate", "check"].includes(cmd)) {
    process.stderr.write("usage: node windows-actions.mts generate|check <actions.mts> <dir>\n");
    return 2;
  }
  const set = ((await import(pathToFileURL(resolve(decl)).href)) as { default: ActionSet }).default;
  const files = renderWindows(set, SPIKE_OPTIONS);
  if (cmd === "generate") {
    for (const [rel, text] of files) {
      await mkdir(join(dir, rel, ".."), { recursive: true });
      await writeFile(join(dir, rel), text);
    }
    process.stdout.write(`windows-actions: wrote ${files.size} files into ${dir}.\n`);
    return 0;
  }
  const found = await readTree(dir);
  const problems: string[] = [];
  for (const [rel, text] of files) if (found.get(rel) !== text) problems.push(`${rel}: ${found.has(rel) ? "differs from" : "is missing from"} what the declarations generate`);
  for (const rel of found.keys()) if (!files.has(rel)) problems.push(`${rel}: is not generated by the declarations`);
  for (const p of problems) process.stderr.write(`windows-actions: ${p}. Fix: node spikes/shared/windows-actions.mts generate ${decl} ${dir}\n`);
  if (!problems.length) process.stdout.write(`windows-actions: ${files.size} files match the declarations.\n`);
  return problems.length ? 1 : 0;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main(process.argv.slice(2)).then((c) => process.exit(c), (e: unknown) => {
    process.stderr.write(`windows-actions: ${e instanceof Error ? e.message : String(e)}\n`);
    process.exit(1);
  });
}
