// Ouro Shop's window, in code: the shopping list (a Card of Items with Badges, and one Button per declared action),
// then the gallery of every Button and Badge tone and variant, and of the fields and lists, and a card the agent
// composed, as Ouro's composed tree and as A2UI messages. What a person presses is what the agent doors list, because every action button comes from the generated
// catalog.
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
    private readonly TextBlock data = new() { TextWrapping = TextWrapping.Wrap };

    /// <param name="page">"list" (the default), "gallery", "fields", "composed" or "a2ui": each fits a 1024 x 768 screen, as CI runners have.</param>
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

        var gallery = new OuroCard { Title = "Gallery", Description = "Every Button and Badge tone and variant", Variant = OuroFrame.Plain, Density = OuroDensity.Small, Content = Column(16, buttons, badges) };
        AutomationProperties.SetAutomationId(gallery, "card.gallery");

        var root = page switch
        {
            "gallery" => Column(16, gallery, status),
            "fields" => Column(16, Fields(), status),
            "composed" => Column(16, Composed(), status, data),
            "a2ui" => Column(16, A2UI(), status, data),
            _ => Column(16, heading, list, status),
        };
        root.Padding = new Thickness(32);
        var card = page is "composed" or "a2ui";
        root.MaxWidth = card ? double.PositiveInfinity : 900;
        root.HorizontalAlignment = card ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
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

    private void Show(OuroActionInfo? action, OuroDoor door, OuroOutcome outcome) => Say(outcome.State switch
    {
        OuroOutcomeState.Refused => $"Refused at the {door} door: {outcome.Summary}",
        OuroOutcomeState.Done => $"{action?.Label ?? "Done"}, through the {door} door: {OuroJson.Write(outcome.Body)}",
        _ => $"{outcome.State} at the {door} door: {outcome.Summary}",
    });

    private void Say(string words)
    {
        status.Text = words;
        if (FrameworkElementAutomationPeer.FromElement(status) is { } peer) peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    /// <summary>
    /// A card Ouro Shop's agent composed (Composed/card.json, an MCP tool result), drawn against the app's catalog. Its
    /// buttons run through the app's runtime at the person's door, as the list's own buttons do; the person-only one says
    /// where the person does it and sends nothing. Under it, two buttons stand in for the agent sending an update to the
    /// card (Composed/update.json) and one for a node the card lacks (Composed/update-missing.json); the line at the bottom
    /// shows the data model the fields write.
    /// </summary>
    private StackPanel Composed()
    {
        var surface = new OuroComposedSurface
        {
            OnPress = async (press, _) =>
            {
                var outcome = await App.Runtime.InvokeAsync(new OuroCall(press.Action, press.Input, OuroDoor.Person) { Caller = "person" });
                Say($"{press.Label}{(press.Confirmed ? ", confirmed," : "")} pressed with {OuroJson.Write(press.Input)}: {outcome.State}");
            },
            OnHandoff = handoff => Say($"Handed off: {OuroActions.Catalog.Find(handoff.Action)?.Label ?? handoff.Action} is done {handoff.How}. Nothing was sent."),
        };
        AutomationProperties.SetAutomationId(surface, "composed.surface");
        AutomationProperties.SetAutomationId(data, "composed.data");
        surface.DataChanged += (_, model) => data.Text = OuroJson.Write(model);
        var catalog = OuroComposedCatalog.Load(Embedded("catalog.json"));
        var problems = surface.Show(catalog, OuroComposedResult.From(Embedded("card.json"))!);
        data.Text = problems.Count > 0 ? $"The agent's card doesn't validate: {string.Join("; ", problems)}" : OuroJson.Write(surface.Data);
        var updates = 0;
        OuroButton Agent(string label, string id, string file)
        {
            var b = new OuroButton { Label = label, Tone = OuroTone.Secondary, Variant = OuroVariant.Outline };
            AutomationProperties.SetAutomationId(b, id);
            b.Click += (_, _) =>
            {
                var refused = surface.Show(catalog, OuroComposedResult.From(Embedded(file))!);
                updates++;
                Say(refused.Count > 0 ? $"Update {updates} refused: {string.Join("; ", refused)}" : $"Update {updates} applied.");
            };
            return b;
        }
        var agent = Row(12);
        agent.Children.Add(Agent("Agent: update the card", "agent.update", "update.json"));
        agent.Children.Add(Agent("Agent: update a node the card lacks", "agent.missing", "update-missing.json"));
        return Column(16, surface, agent);
    }

    /// <summary>
    /// The same card as A2UI messages (Composed/card.a2ui.json, an MCP tool result's <c>structuredContent.a2ui</c>), drawn
    /// against the same catalog: core's A2UI adapter resolves them into the composed tree, so it looks and presses the same.
    /// Each press arrives as A2UI's action message, which the app runs through its runtime at the person's door; the status
    /// line says what the message carried.
    /// </summary>
    private StackPanel A2UI()
    {
        var surface = new OuroComposedSurface
        {
            OnA2UIAction = async (message, _) =>
            {
                var action = message["action"]!;
                var name = action["name"]!.GetValue<string>();
                var context = (JsonObject)action["context"]!.DeepClone();
                var source = action["sourceComponentId"]!.GetValue<string>();
                var consent = action["metadata"]!["extensions"]![OuroA2UI.ActionExtension]!;
                var outcome = await App.Runtime.InvokeAsync(new OuroCall(name, context, OuroDoor.Person) { Caller = "person" });
                Say($"Sent A2UI action {name} from {source} with {OuroJson.Write(context)}, by {consent["by"]!.GetValue<string>()}{(consent["confirmed"] is not null ? ", confirmed" : "")}: {outcome.State}");
            },
            OnHandoff = handoff => Say($"Handed off: {OuroActions.Catalog.Find(handoff.Action)?.Label ?? handoff.Action} is done {handoff.How}. Nothing was sent."),
        };
        AutomationProperties.SetAutomationId(surface, "composed.surface");
        AutomationProperties.SetAutomationId(data, "composed.data");
        surface.DataChanged += (_, model) => data.Text = OuroJson.Write(model);
        var catalog = OuroComposedCatalog.Load(Embedded("catalog.json"));
        IReadOnlyList<OuroA2UIProblem> problems = OuroA2UI.TryReadToolResult(Embedded("card.a2ui.json"), out var messages)
            ? surface.ShowA2UI(catalog, messages)
            : [new("VALIDATION_FAILED", "", "The tool result carries no structuredContent.a2ui.")];
        data.Text = problems.Count > 0 ? $"The agent's A2UI card doesn't validate: {string.Join("; ", problems)}" : OuroJson.Write(surface.Data);
        return Column(16, surface);
    }

    private static JsonNode Embedded(string name)
    {
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream($"OuroShop.Composed.{name}")!;
        return JsonNode.Parse(stream)!;
    }

    /// <summary>The gallery's second page: TextField, Checkbox, Separator and DescriptionList, in core's spec cases.</summary>
    private static OuroCard Fields()
    {
        T Id<T>(T e, string id) where T : FrameworkElement
        {
            AutomationProperties.SetAutomationId(e, id);
            return e;
        }
        var fields = Column(16,
            Id(new OuroTextField { Label = "Email", Type = OuroTextFieldType.Email, IsRequired = true, Description = "We send your recipes here.", Placeholder = "you@example.com" }, "gallery.textfield.email"),
            Id(new OuroTextField { Label = "Recipe title", Error = "Give the recipe a title." }, "gallery.textfield.error"),
            Id(new OuroTextField { Label = "Note", IsMultiline = true, Text = "Use the ripe lemons first." }, "gallery.textfield.note"));
        var choices = Column(4,
            Id(new OuroCheckbox { Label = "I agree to the terms", IsRequired = true }, "gallery.checkbox.terms"),
            Id(new OuroCheckbox { Label = "Whole basket", IsChecked = null }, "gallery.checkbox.mixed"),
            Id(new OuroCheckbox { Label = "Vegetarian", IsChecked = true, Description = "No meat or fish." }, "gallery.checkbox.checked"));
        var lines = Column(12,
            Id(new OuroSeparator { Label = "or" }, "gallery.separator.or"),
            Id(new OuroSeparator(), "gallery.separator.line"),
            Id(new OuroSeparator { Label = "New", Tone = OuroTone.Primary }, "gallery.separator.new"));
        var details = Id(new OuroDescriptionList { Items = [new("Prep time", "20 minutes"), new("Serves", "6")] }, "gallery.descriptionlist.recipe");
        var grid = new Grid { ColumnSpacing = 32, MinWidth = 720, ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() } };
        var right = Column(16, choices, lines, details);
        Grid.SetColumn(right, 1);
        grid.Children.Add(fields);
        grid.Children.Add(right);
        return Id(new OuroCard { Title = "Fields and lists", Description = "TextField, Checkbox, Separator and DescriptionList", Variant = OuroFrame.Plain, Density = OuroDensity.Small, Content = grid }, "card.fields");
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
