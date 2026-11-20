// A yes-or-no for a process with no window of its own, such as an app's MCP server: Windows' own message box, on top,
// with No as the default button. The runtime asks it before a consequential action an agent prepared.
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Ouro.Actions;

/// <summary>Asks the person with a Windows message box.</summary>
[SupportedOSPlatform("windows")]
public static partial class OuroMessageBox
{
    private const uint YesNo = 0x4, Question = 0x20, DefaultSecond = 0x100, Foreground = 0x10000, Topmost = 0x40000, Yes = 6;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int Show(nint owner, string text, string caption, uint type);

    /// <summary>An <see cref="OuroConfirm"/> that shows the action's label, its declared confirm question, what was asked and by whom, and Yes or No.</summary>
    public static OuroConfirm Confirm(string app, OuroActionCatalog catalog) => (action, input, call, _) => Task.Run(() =>
    {
        var details = string.Join("\n", input.Where(p => p.Key != "idempotencyKey").Select(p => $"  {p.Key}: {p.Value}"));
        var text = $"{(call.Caller is { Length: > 0 } c ? c : "An agent")} asks {app} to: {catalog.ConfirmText(action)}\n{(details.Length > 0 ? details + "\n" : "")}\nDo you want this?";
        return Show(0, text, $"{app}: {action.Label}", YesNo | Question | DefaultSecond | Foreground | Topmost) == Yes;
    });
}
