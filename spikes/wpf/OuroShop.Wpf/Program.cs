// The runner-up: the same window in WPF with its Fluent theme (ThemeMode.System), so the two toolkits are measured on
// the same slice: the same declarations, handler, automation ids and status line.
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Ouro.Actions.Generated;
using Ouro.Shop;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        ShoppingHandler.Bind();
        var app = new Application { ThemeMode = ThemeMode.System };
        app.Run(new ShopWindow());
    }
}

public sealed class ShopWindow : Window
{
    private readonly TextBox name;
    private readonly TextBlock status;

    public ShopWindow()
    {
        Title = "Ouro Shop (WPF Fluent spike)";
        Width = 640;
        Height = 420;
        var title = new TextBlock { Text = "Shopping list", FontSize = 28, FontWeight = FontWeights.SemiBold };
        name = new TextBox { Text = "oat milk", MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(name, "input.name");
        AutomationProperties.SetName(name, "Item");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var action in OuroActions.All)
        {
            var button = new Button { Content = new TextBlock { Text = action.Label, TextWrapping = TextWrapping.Wrap }, MinWidth = 44, MinHeight = 44, MaxWidth = 220, Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 0, 12, 0) };
            AutomationProperties.SetName(button, action.Label);
            AutomationProperties.SetAutomationId(button, action.AutomationId);
            AutomationProperties.SetHelpText(button, action.Description);
            button.Click += async (_, _) => await RunAsync(action);
            buttons.Children.Add(button);
        }
        status = new TextBlock { Text = "Ready", TextWrapping = TextWrapping.Wrap, MaxWidth = 560 };
        AutomationProperties.SetAutomationId(status, "status");
        var root = new StackPanel { Margin = new Thickness(32) };
        foreach (var e in new UIElement[] { title, name, buttons, status })
        {
            if (e is FrameworkElement f) f.Margin = new Thickness(0, 0, 0, 16);
            root.Children.Add(e);
        }
        Content = root;
    }

    private async Task RunAsync(OuroActionInfo action)
    {
        var input = action.Id == "add_shopping_list_item" ? new JsonObject { ["name"] = name.Text } : new JsonObject();
        var outcome = await OuroActions.InvokeAsync(action.Id, input, OuroDoor.Person);
        status.Text = $"{action.Label}, through the Person door: {outcome.ToJsonString()}";
    }
}
