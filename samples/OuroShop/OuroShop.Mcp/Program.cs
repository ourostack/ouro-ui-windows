// Ouro Shop's MCP server over stdio. tools/list is core's mcp.json; a person-only call answers core's typed refusal;
// a consequential call asks the person with a Windows message box first. Every call is audited beside the app's.
using Ouro.Actions;
using OuroShop;
using OuroShop.Actions;

var audit = new OuroJsonlAudit(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OuroShop", "audit.jsonl"));
var runtime = new OuroActionRuntime(OuroActions.Catalog, ShoppingHandler.Handle, audit) { Confirm = OuroMessageBox.Confirm("Ouro Shop") };
await new OuroMcpServer(runtime).RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput());
