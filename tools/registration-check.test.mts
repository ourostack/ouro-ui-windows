import assert from "node:assert/strict";
import { test } from "node:test";
import { check } from "./registration-check.mts";

const installed = { extensions: [
  { app: "App", category: "windows.protocol", protocol: "ouro-shop" },
  { app: "App", category: "windows.appExecutionAlias", alias: "OuroShop.exe" },
  { app: "App", category: "windows.appExtension", name: "com.microsoft.windows.ai.actions" },
  { app: "Mcp", category: "windows.appExecutionAlias", alias: "OuroShop.Mcp.exe" },
  { app: "Mcp", category: "windows.appExtension", name: "com.microsoft.windows.ai.mcpServer" },
] };
const actions = { actions: [{ id: "Ouro.Add" }, { id: "Ouro.Share" }] };
const mcp = { tools: [{ name: "add" }, { name: "share" }] };

test("a package that registered both doors, with the files the declarations generate, passes", () => {
  const result = check(installed, actions, actions, mcp, mcp);
  assert.deepEqual(result.problems, []);
  assert.deepEqual(result.doors, ["app-actions", "mcp"]);
});

test("a door Windows did not register fails, naming it", () => {
  const without = (name: string) => ({ extensions: installed.extensions.filter((e) => e.name !== name) });
  assert.match(check(without("com.microsoft.windows.ai.actions"), actions, actions, mcp, mcp).problems.join(), /App Actions extension/);
  assert.match(check(without("com.microsoft.windows.ai.mcpServer"), actions, actions, mcp, mcp).problems.join(), /MCP server extension/);
});

test("a missing scheme or alias fails", () => {
  const without = (key: string) => ({ extensions: installed.extensions.filter((e) => !(key in e)) });
  assert.match(check(without("protocol"), actions, actions, mcp, mcp).problems.join(), /URI scheme/);
  assert.match(check({ extensions: installed.extensions.filter((e) => e.alias !== "OuroShop.Mcp.exe") }, actions, actions, mcp, mcp).problems.join(), /execution alias/);
});

test("an installed action or tool list that differs from the generated one fails, as does an empty one", () => {
  assert.match(check(installed, { actions: [{ id: "Ouro.Add" }] }, actions, mcp, mcp).problems.join(), /App Actions are \[Ouro.Add\]/);
  assert.match(check(installed, actions, actions, { tools: [] }, mcp).problems.join(), /MCP tools are \[\]/);
  assert.match(check(installed, { actions: [] }, { actions: [] }, mcp, mcp).problems.join(), /nothing to compare/);
});
