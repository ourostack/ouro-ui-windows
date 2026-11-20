// Ouro Shop's start: one action runtime for the app, a window, and an App Action launch when there is one.
// Arguments (for CI and for trying it): --theme light|dark, --page list|gallery|fields|nav|overlays|confirms|forms|composed|a2ui, --self-report (the controls report what they drew to UI
// Automation), and an ouro-shop:// URI when run unpackaged (packaged, Windows passes the URI as protocol activation).
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Ouro.Actions;
using Ouro.UI.Windows;
using OuroShop.Actions;

namespace OuroShop;

public partial class App : Application
{
    private static readonly string[] Args = Environment.GetCommandLineArgs().Skip(1).ToArray();
    private MainWindow? window;

    /// <summary>Where the app writes an exception it could not handle, so CI (and a person) can read why it stopped.</summary>
    public static string CrashLog { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OuroShop", "crash.txt");

    private static void Crashed(object? error)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(CrashLog)!); File.AppendAllText(CrashLog, $"[{DateTimeOffset.Now:O}] {error}\n\n"); } catch (IOException) { }
    }

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crashed(e.ExceptionObject);
        UnhandledException += (_, e) => Crashed(e.Exception);
        var theme = Arg("--theme");
        if (theme == "dark") RequestedTheme = ApplicationTheme.Dark;
        else if (theme == "light") RequestedTheme = ApplicationTheme.Light;
        OuroUI.SelfReport = Args.Contains("--self-report");
        InitializeComponent();
    }

    private static string? Arg(string name) => Array.IndexOf(Args, name) is var i and >= 0 && i + 1 < Args.Length ? Args[i + 1] : null;

    /// <summary>The app's one runtime: the buttons, the App Actions and the MCP server's calls all go through one like it.</summary>
    public static OuroActionRuntime Runtime { get; } = new(OuroActions.Catalog, ShoppingHandler.Handle,
        new OuroJsonlAudit(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OuroShop", "audit.jsonl")));

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try { Launch(); } catch (Exception e) { Crashed(e); throw; }
    }

    private void Launch()
    {
        var w = window = new MainWindow(Arg("--page") ?? "list");
        Runtime.Confirm = OuroPerson.Dialog(() => w.Content?.XamlRoot);
        w.Activate();
        var activated = AppInstance.GetCurrent().GetActivatedEventArgs();
        var uri = activated.Kind == ExtendedActivationKind.Protocol && activated.Data is global::Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs p
            ? p.Uri
            : Args.Where(a => a.StartsWith(OuroActions.Catalog.Scheme + "://", StringComparison.Ordinal)).Select(a => new Uri(a)).FirstOrDefault();
        if (uri is not null) w.Launched(uri);
    }
}
