// Asks Windows which App Actions are registered (Windows.AI.Actions.Hosting.ActionCatalog.GetAllActions) and prints
// them as JSON, so CI can see whether the package's actions reached the system's catalog. On a machine without the
// App Actions runtime it prints the error instead, and exits 3.
using System.Text.Json;
using System.Text.Json.Nodes;

try
{
    var runtime = Microsoft.AI.Actions.Helpers.ActionRuntimeFactory.CreateActionRuntime();
    var all = runtime.ActionCatalog.GetAllActions();
    var list = new JsonArray();
    foreach (var d in all) list.Add(new JsonObject { ["id"] = d.Id, ["description"] = d.Description });
    Console.WriteLine(new JsonObject { ["ok"] = true, ["os"] = Environment.OSVersion.VersionString, ["count"] = list.Count, ["actions"] = list }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (Exception e)
{
    Console.WriteLine(new JsonObject { ["ok"] = false, ["os"] = Environment.OSVersion.VersionString, ["error"] = $"{e.GetType().FullName}: {e.Message}", ["hresult"] = $"0x{e.HResult:X8}" }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    return 3;
}
