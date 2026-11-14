// Ouro Shop's window, in code: the shopping list (a Card of Items with Badges, and one Button per declared action),
// then the gallery of every Button and Badge tone and variant. What a person presses is what the agent doors list,
// because every action button comes from the generated catalog.
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Ouro.Actions;
using Ouro.UI.Windows;
using OuroShop.Actions;

namespace OuroShop;

public sealed class MainWindow : Window
{
    private readonly TextBox name;
    private readonly TextBlock status;

    /// <param name="page">"list" (the default) or "gallery": each fits a 1024 x 768 screen, as CI runners have.</param>
    public MainWindow(string page = "list")
    {
        Title = "Ouro Shop";
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1000, 740));
        AppWindow.Move(new global::Windows.Graphics.PointInt32(0, 0));

        var heading = new TextBlock { Text = "Shopping list", Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] };
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);

        name = new TextBox { Header = "Item", Text = "oat milk", MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(name, "input.name");

        status = new TextBlock { Text = "Ready", TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(status, "status");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);

        var actions = Row(12);
        foreach (var action in OuroActions.Catalog.Actions)
        {
            var button = new OuroButton
            {
                Action = action,
                Runtime = App.Runtime,
                Tone = action.Consent == OuroConsent.PersonOnly ? OuroTone.Secondary : OuroTone.Primary,
                Variant = action.Consent == OuroConsent.PersonOnly ? OuroVariant.Soft : OuroVariant.Solid,
                Input = () => Input(action),
            };
            button.Ran += (_, outcome) => Show(action, OuroDoor.Person, outcome);
            actions.Children.Add(button);
        }

        var list = new OuroCard
        {
            Title = "This week",
            Description = "Three things to pick up",
            Action = new OuroBadge { Text = "3", Tone = OuroTone.Primary },
            Content = Column(0,
                Item("Oat milk", "2 cartons", "", new OuroBadge { Text = "Vegan", Tone = OuroTone.Success, Variant = OuroVariant.Soft }, pressable: true),
                Item("Eggs", "A dozen", "", new OuroBadge { Text = "12", Tone = OuroTone.Secondary }, pressable: false),
                Item("Lemons", "For the orzo", "", new OuroBadge { Text = "Low", Tone = OuroTone.Warning, Variant = OuroVariant.Outline }, pressable: true)),
            Footer = Column(12, name, actions),
        };
        AutomationProperties.SetAutomationId(list, "card.list");

        var buttons = Column(8);
        foreach (var variant in Enum.GetValues<OuroVariant>())
        {
            var row = Row(8);
            foreach (var tone in Enum.GetValues<OuroTone>())
            {
                var b = new OuroButton { Label = $"{tone} {variant}".ToLowerInvariant(), Tone = tone, Variant = variant };
                AutomationProperties.SetAutomationId(b, $"gallery.button.{tone}.{variant}".ToLowerInvariant());
                row.Children.Add(b);
            }
            buttons.Children.Add(row);
        }

        var badges = Column(8);
        foreach (var variant in new[] { OuroVariant.Solid, OuroVariant.Soft, OuroVariant.Outline })
        {
            var row = Row(8);
            foreach (var tone in Enum.GetValues<OuroTone>())
            {
                var b = new OuroBadge { Text = $"{tone} {variant}".ToLowerInvariant(), Tone = tone, Variant = variant };
                AutomationProperties.SetAutomationId(b, $"gallery.badge.{tone}.{variant}".ToLowerInvariant());
                row.Children.Add(b);
            }
            badges.Children.Add(row);
        }

        // TEMP trace: reveal the gallery one row at a time, logging each, to find the one that fails to measure.
        if (page == "gallery")
        {
            var rows = buttons.Children.Concat(badges.Children).Cast<StackPanel>().ToList();
            foreach (var r in rows) foreach (var c in r.Children) c.Visibility = Visibility.Collapsed;
            Activated += async (_, _) =>
            {
                foreach (var r in rows)
                    foreach (var c in r.Children)
                    {
                        await Task.Delay(20);
                        App.Trace($"show {AutomationProperties.GetAutomationId((FrameworkElement)c)}");
                        c.Visibility = Visibility.Visible;
                        Content.UpdateLayout();
                    }
                App.Trace("all shown");
            };
        }

        var gallery = new OuroCard { Title = "Gallery", Description = "Every Button and Badge tone and variant", Variant = OuroFrame.Plain, Density = OuroDensity.Small, Content = Column(16, buttons, badges) };
        AutomationProperties.SetAutomationId(gallery, "card.gallery");

        var root = page == "gallery" ? Column(16, gallery, status) : Column(16, heading, list, status);
        root.Padding = new Thickness(32);
        root.MaxWidth = 900;
        root.HorizontalAlignment = HorizontalAlignment.Left;
        Content = new Grid
        {
            Background = OuroUI.Brush(OuroUI.HighContrast ? OuroUI.System(global::Windows.UI.ViewManagement.UIElementType.Window) : OuroUI.Argb(OuroUI.Theme.Colors(Application.Current.RequestedTheme == ApplicationTheme.Dark).Background)),
            Children = { new ScrollViewer { Content = root } },
        };
    }

    /// <summary>An App Action's launch: the door decodes it, the runtime runs it, the status line says what happened.</summary>
    public async void Launched(Uri uri)
    {
        if (OuroAppActionUri.Read(OuroActions.Catalog, uri) is not { } call)
        {
            status.Text = $"{uri} isn't one of Ouro Shop's actions.";
            return;
        }
        await Task.Delay(300);
        var outcome = await App.Runtime.InvokeAsync(call);
        Show(OuroActions.Catalog.Find(call.ActionId), OuroDoor.AppAction, outcome);
    }

    private JsonObject Input(OuroActionInfo action) => action.Id switch
    {
        "add_shopping_list_item" => new JsonObject { ["name"] = name.Text },
        "share_shopping_list" => new JsonObject { ["with"] = "sam@example.com" },
        _ => new JsonObject(),
    };

    private void Show(OuroActionInfo? action, OuroDoor door, OuroOutcome outcome)
    {
        status.Text = outcome.State switch
        {
            OuroOutcomeState.Refused => $"Refused at the {door} door: {outcome.Summary}",
            OuroOutcomeState.Done => $"{action?.Label ?? "Done"}, through the {door} door: {OuroJson.Write(outcome.Body)}",
            _ => $"{outcome.State} at the {door} door: {outcome.Summary}",
        };
        if (FrameworkElementAutomationPeer.FromElement(status) is { } peer) peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private OuroItem Item(string title, string description, string glyph, UIElement badge, bool pressable)
    {
        var item = new OuroItem { Title = title, Description = description, MediaSize = OuroMediaSize.Icon, Media = new FontIcon { Glyph = glyph, FontSize = 18 }, Actions = badge };
        AutomationProperties.SetAutomationId(item, "item." + title.ToLowerInvariant().Replace(' ', '-'));
        if (pressable) item.Click += (_, _) => status.Text = $"Opened {title}";
        return item;
    }

    private static StackPanel Row(double spacing) => new() { Orientation = Orientation.Horizontal, Spacing = spacing };

    private static StackPanel Column(double spacing, params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = spacing };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }
}
