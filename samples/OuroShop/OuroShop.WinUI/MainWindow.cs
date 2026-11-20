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

    /// <param name="page">"list" (the default), "gallery", "fields", "nav", "overlays", "confirms", "composed" or "a2ui": each fits a 1024 x 768 screen, as CI runners have.</param>
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
            "nav" => Column(16, Navigation(), status),
            "overlays" => Column(16, Overlays(), status),
            "confirms" => Column(16, Confirms(), status),
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
            : new OuroA2UIProblem[] { new("VALIDATION_FAILED", "", "The tool result carries no structuredContent.a2ui.") };
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
            Id(new OuroTextField { Label = "Email", Type = OuroTextFieldType.Email, IsRequired = true, Description = "We send your recipes here.", Placeholder = "you@example.com" }, "gallery.text-field.email-with-description"),
            Id(new OuroTextField { Label = "Password", Type = OuroTextFieldType.Password, Text = "lemon-orzo" }, "gallery.text-field.password"),
            Id(new OuroTextField { Label = "Recipe title", Error = "Give the recipe a title." }, "gallery.text-field.with-error"),
            Id(new OuroTextField { Label = "Note", IsMultiline = true, Text = "Use the ripe lemons first." }, "gallery.text-field.note"));
        var choices = Column(4,
            Id(new OuroCheckbox { Label = "I agree to the terms", IsRequired = true }, "gallery.checkbox.terms"),
            Id(new OuroCheckbox { Label = "Whole basket", IsChecked = null }, "gallery.checkbox.mixed"),
            Id(new OuroCheckbox { Label = "Vegetarian", IsChecked = true, Description = "No meat or fish." }, "gallery.checkbox.checked"));
        var lines = Column(12,
            Id(new OuroSeparator { Label = "or" }, "gallery.separator.or"),
            Id(new OuroSeparator(), "gallery.separator.line"),
            Id(new OuroSeparator { Label = "New", Tone = OuroTone.Primary }, "gallery.separator.new"));
        var details = Id(new OuroDescriptionList { Items = new OuroDescriptionItem[] { new("Prep time", "20 minutes"), new("Serves", "6") } }, "gallery.descriptionlist.recipe");
        var tabs = Column(16,
            Id(new OuroTabs { Label = "Recipe sections", Value = "ingredients", Items = new OuroTabItem[] { new("ingredients", "Ingredients"), new("method", "Method") }, Content = new TextBlock { Text = "The ingredient list", Margin = new Thickness(0, 12, 0, 0) } }, "gallery.tabs.recipe-sections"),
            Id(new OuroTabs { Label = "Settings", Value = "profile", Vertical = true, Items = new OuroTabItem[] { new("profile", "Profile"), new("billing", "Billing", Disabled: true) }, Content = new TextBlock { Text = "Your profile", Margin = new Thickness(16, 0, 0, 0) } }, "gallery.tabs.vertical-with-disabled"));
        var grid = new Grid { ColumnSpacing = 32, MinWidth = 720, ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() } };
        var tabBars = Column(16,
            Id(new OuroTabBar { Label = "Main", Current = "Recipes", Width = 320, Items = new OuroTabBarItem[] { new("Home", "\uE80F"), new("Recipes", "\uE8F1"), new("Shopping", "\uE719") }, Accessory = new("Search", "\uE721") }, "gallery.tab-bar.main-with-search"),
            Id(new OuroTabBar { Label = "Main", Current = "Home", Width = 320, Items = new OuroTabBarItem[] { new("Home", "\uE80F"), new("Settings", "\uE713") } }, "gallery.tab-bar.two-tabs"));
        var right = Column(16, choices, lines, details, tabs);
        Grid.SetColumn(right, 1);
        grid.Children.Add(Column(16, fields, tabBars));
        grid.Children.Add(right);
        return Id(new OuroCard { Title = "Fields and lists", Description = "TextField, Checkbox, Separator, DescriptionList, Tabs and TabBar", Variant = OuroFrame.Plain, Density = OuroDensity.Small, Content = grid }, "card.fields");
    }

    /// <summary>The gallery's third page: Sidebar, open and as a rail, in core's spec cases.</summary>
    private static OuroCard Navigation()
    {
        T Id<T>(T e, string id) where T : FrameworkElement
        {
            AutomationProperties.SetAutomationId(e, id);
            return e;
        }
        var open = Id(new OuroSidebar
        {
            Label = "Main",
            Current = "Recipes",
            Header = new TextBlock { Text = "Spoonjoy", FontSize = 18, Margin = new Thickness(16, 0, 0, 0) },
            Sections = new OuroSidebarSection[]
            {
                new("", new OuroSidebarItem[] { new("Recipes", "\uE8F1"), new("Inbox", "\uE715", "12") }),
                new("Library", new OuroSidebarItem[] { new("Cookbooks", "\uE82D") }),
            },
        }, "gallery.sidebar.sections-and-badge");
        var rail = Id(new OuroSidebar
        {
            Label = "Main",
            Current = "Recipes",
            Collapsed = true,
            Header = new TextBlock { Text = "Spoonjoy", FontSize = 18 },
            Sections = new OuroSidebarSection[] { new("", new OuroSidebarItem[] { new("Recipes", "\uE8F1"), new("Cookbooks", "\uE82D") }) },
        }, "gallery.sidebar.collapsed-rail");
        var row = Row(32);
        row.VerticalAlignment = VerticalAlignment.Top;
        row.Children.Add(open);
        row.Children.Add(rail);
        return Id(new OuroCard { Title = "Navigation", Description = "Sidebar, open and as a rail", Variant = OuroFrame.Plain, Density = OuroDensity.Small, Content = row }, "card.nav");
    }

    /// <summary>The gallery's fourth page: Dialog and Sheet, already open and drawn in the page, in core's spec cases.</summary>
    private static OuroCard Overlays()
    {
        T Id<T>(T e, string id) where T : FrameworkElement
        {
            AutomationProperties.SetAutomationId(e, id);
            return e;
        }
        var share = OuroActions.Catalog.Find("share_shopping_list");
        var left = Column(16, Id(new OuroDialog { Title = "Rename list", Description = "Pick a name your household will recognise.", PanelContent = new OuroTextField { Label = "List name", Text = "Weekly shop" }, Footer = new OuroButton { Label = "Save" } }, "gallery.dialog.rename"));
        var right = Column(16,
            Id(new OuroDialog { Title = "Recipe photo", HideTitle = true, CloseLabel = "Close photo", PanelContent = new TextBlock { Text = "A lemon tart.", TextWrapping = TextWrapping.Wrap } }, "gallery.dialog.hidden"),
            Id(new OuroSheet { Title = "Filters", Description = "Narrow the recipes.", Side = OuroSheetSide.Bottom, PanelContent = new OuroCheckbox { Label = "Vegetarian" } }, "gallery.sheet.filters"));
        Grid.SetColumn(right, 1);
        var panels = new Grid { ColumnSpacing = 16, MinWidth = 720, ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() }, Children = { left, right } };
        return Id(new OuroCard { Title = "Overlays", Description = "Dialog and Sheet", Variant = OuroFrame.Plain, Density = OuroDensity.Small, Content = panels }, "card.overlays");
    }

    /// <summary>The gallery's fifth page: ConfirmDialog, asking about a declared action and about a step that can't be undone.</summary>
    private static OuroCard Confirms()
    {
        T Id<T>(T e, string id) where T : FrameworkElement
        {
            AutomationProperties.SetAutomationId(e, id);
            return e;
        }
        var share = OuroActions.Catalog.Find("share_shopping_list");
        var left = Column(16,
            Id(new OuroConfirmDialog { Title = "Share your list?", ConfirmLabel = "Share", Action = share, Catalog = OuroActions.Catalog }, "gallery.confirm.share"));
        var right = Column(16, Id(new OuroConfirmDialog { Title = "Delete this recipe?", Description = "This can't be undone.", ConfirmLabel = "Delete", Tone = OuroTone.Destructive }, "gallery.confirm.delete"));
        Grid.SetColumn(right, 1);
        var panels = new Grid { ColumnSpacing = 16, MinWidth = 720, ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() }, Children = { left, right } };
        return Id(new OuroCard { Title = "Confirmations", Description = "ConfirmDialog", Variant = OuroFrame.Plain, Density = OuroDensity.Small, Content = panels }, "card.confirms");
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
