// The spike's window, written in code: a field, one Ouro button per declared action, and a status line that says what
// happened and through which door. Every button comes from the declarations, so what a person presses is what the
// agent doors list.
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Ouro.Actions.Generated;
using Ouro.UI.WinUI;

namespace OuroShop;

public sealed class MainWindow : Window
{
    private readonly TextBox name;
    private readonly TextBlock status;

    public MainWindow()
    {
        Title = "Ouro Shop (WinUI spike)";
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(640, 420));

        var title = new TextBlock { Text = "Shopping list", Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        name = new TextBox { Header = "Item", Text = "oat milk", MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(name, "input.name");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var action in OuroActions.All)
        {
            var button = new OuroButton(action, action.Consent == OuroConsent.PersonOnly ? OuroButtonVariant.Secondary : OuroButtonVariant.Primary);
            button.Click += async (_, _) => await RunAsync(action, OuroDoor.Person, null);
            buttons.Children.Add(button);
        }

        status = new TextBlock { Text = "Ready", TextWrapping = TextWrapping.Wrap, MaxWidth = 560 };
        AutomationProperties.SetAutomationId(status, "status");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);

        var root = new StackPanel { Spacing = 16, Padding = new Thickness(32) };
        root.Children.Add(title);
        root.Children.Add(name);
        root.Children.Add(buttons);
        root.Children.Add(status);
        Content = root;
    }

    private async Task RunAsync(OuroActionInfo action, OuroDoor door, JsonObject? input)
    {
        input ??= action.Id == "add_shopping_list_item" ? new JsonObject { ["name"] = name.Text } : new JsonObject();
        JsonNode outcome;
        try { outcome = await OuroActions.InvokeAsync(action.Id, input, door); }
        catch (Exception e) { outcome = new JsonObject { ["state"] = "failed", ["summary"] = e.Message }; }
        status.Text = outcome["state"]?.GetValue<string>() == "refused"
            ? $"Refused at the {door} door: {outcome["summary"]}"
            : $"{action.Label}, through the {door} door: {outcome.ToJsonString()}";
        if (FrameworkElementAutomationPeer.FromElement(status) is { } peer) peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    /// <summary>An App Action's launch: ouro-shop-spike://action/&lt;id&gt;?&lt;inputs&gt;&amp;token=…</summary>
    public Task HandleUriAsync(Uri uri)
    {
        var id = uri.AbsolutePath.Trim('/');
        var action = OuroActions.Find(id);
        if (uri.Host != "action" || action is null)
        {
            status.Text = $"No declared action at {uri}";
            return Task.CompletedTask;
        }
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var input = new JsonObject();
        foreach (var key in query.AllKeys.OfType<string>().Where(k => k != "token")) input[key] = query[key];
        return RunAsync(action, OuroDoor.AppAction, input);
    }
}
