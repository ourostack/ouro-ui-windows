using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Ouro.Shop;

namespace OuroShop;

public partial class App : Application
{
    private MainWindow? window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ShoppingHandler.Bind();
        window = new MainWindow();
        window.Activate();
        // An App Action launches the packaged app through its URI scheme. Unpackaged, CI passes the same URI as an argument.
        var activated = AppInstance.GetCurrent().GetActivatedEventArgs();
        Uri? uri = activated.Kind == ExtendedActivationKind.Protocol && activated.Data is Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs p
            ? p.Uri
            : Environment.GetCommandLineArgs().Skip(1).Where(a => a.StartsWith(Ouro.Actions.Generated.OuroActions.Scheme + "://", StringComparison.Ordinal)).Select(a => new Uri(a)).FirstOrDefault();
        if (uri is not null) _ = window.HandleUriAsync(uri);
    }
}
