// Ouro's composed tree drawn with Ouro HX on Windows: web's and Apple's ComposedSurface. An agent composes the tree (in
// an MCP tool result's structuredContent.ouro, read with OuroComposedResult); the surface draws it only once it validates
// against the app's catalog (OuroComposed.ValidateTree), so every button is one of the app's declared actions with its
// consent class. Read and write buttons press at once; a consequential one shows what it will send in a dialog and
// presses only when the person confirms; a person-only one hands the person to the app's own screen and never presses.
// Fields write the person's answers into the data model, and a button bound to one sends what they entered.
//
// The surface is the person's: it has no way for an agent to press, and it doesn't read A2UI messages. Agents act through
// the action's own door (App Actions, the MCP server). Decision: core docs/decisions/2026-11-14-composed-ui-own-tree.md.
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Ouro.Actions;

namespace Ouro.UI.Windows;

/// <summary>A person-only button was pressed: take the person to the action's own screen. <c>How</c> is the declaration's words.</summary>
public sealed record OuroComposedHandoff(string Action, string How);

/// <summary>
/// Ouro's composed tree, drawn with Ouro HX: the interface an agent composes for a person, validated against the app's
/// catalog. Pressing a button follows its consent class; nothing an agent sends can relabel a button or skip a confirm.
/// </summary>
/// <example>
/// <code>
/// var surface = new OuroComposedSurface
/// {
///     OnPress = (press, _) => runtime.InvokeAsync(new OuroCall(press.Action, press.Input, OuroDoor.Person) { Caller = "person" }),
///     OnHandoff = handoff => Navigate(handoff.Action),
/// };
/// var problems = surface.Show(catalog, OuroComposedResult.From(toolResult)!);
/// </code>
/// </example>
public sealed partial class OuroComposedSurface : UserControl
{
    private readonly List<Action> paints = [];
    private bool confirming;

    public OuroComposedSurface()
    {
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        OuroUI.Follow(this, Repaint);
    }

    /// <summary>The catalog the tree was checked against.</summary>
    public OuroComposedCatalog? Catalog { get; private set; }

    /// <summary>The tree drawn now, or null when nothing is.</summary>
    public OuroComposedNode? Tree { get; private set; }

    /// <summary>The data model: what the tree's fields start from and write the person's answers to.</summary>
    public JsonObject Data { get; private set; } = new();

    /// <summary>Why the last tree wasn't drawn; empty when it was.</summary>
    public IReadOnlyList<OuroTreeProblem> Problems { get; private set; } = [];

    /// <summary>
    /// Runs a pressed read or write button, and a consequential one once the person confirms. The press carries the
    /// action, the input with each bound field's answer read now, and that the person pressed it. The button is busy
    /// until the task ends.
    /// </summary>
    public Func<OuroPersonPress, OuroComposedNode, Task>? OnPress { get; set; }

    /// <summary>A person-only button: take the person to the action's own screen. It never presses.</summary>
    public Action<OuroComposedHandoff>? OnHandoff { get; set; }

    /// <summary>A field wrote the person's answer into the data model.</summary>
    public event EventHandler<JsonObject>? DataChanged;

    /// <summary>Draws a tool result's tree when it is for this catalog and validates; otherwise draws nothing and returns why.</summary>
    public IReadOnlyList<OuroTreeProblem> Show(OuroComposedCatalog catalog, OuroComposedResult result)
    {
        var problems = result.Problems(catalog);
        return problems.Count > 0 ? Clear(problems) : Show(catalog, result.Tree, result.Data);
    }

    /// <summary>Draws a tree when it validates against the catalog; otherwise draws nothing and returns why.</summary>
    public IReadOnlyList<OuroTreeProblem> Show(OuroComposedCatalog catalog, JsonNode? tree, JsonObject? data = null)
    {
        var problems = OuroComposed.ValidateTree(catalog, tree);
        if (problems.Count > 0) return Clear(problems);
        Catalog = catalog;
        Tree = OuroComposedNode.FromJson(tree);
        Data = (JsonObject?)data?.DeepClone() ?? new JsonObject();
        Problems = [];
        paints.Clear();
        Content = Draw(Tree);
        AutomationProperties.SetName(this, FirstHeading(Tree) ?? "");
        if (IsLoaded) Repaint();
        return Problems;
    }

    private IReadOnlyList<OuroTreeProblem> Clear(IReadOnlyList<OuroTreeProblem> problems)
    {
        Catalog = null;
        Tree = null;
        Data = new JsonObject();
        Problems = problems;
        paints.Clear();
        Content = null;
        return problems;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroComposedSurfaceAutomationPeer(this);

    private void Repaint()
    {
        foreach (var paint in paints) paint();
    }

    private static string? FirstHeading(OuroComposedNode n) =>
        n.Component == "Heading" ? n.Text("text") : n.Children.Select(FirstHeading).FirstOrDefault(h => h is not null);

    private static double Gap(string gap) => gap switch { "none" => 0, "small" => 8, "large" => 24, _ => 16 };

    private static OuroTone Tone(string tone) => tone switch
    {
        "info" => OuroTone.Primary,
        "success" => OuroTone.Success,
        "warning" => OuroTone.Warning,
        "danger" => OuroTone.Destructive,
        _ => OuroTone.Secondary,
    };

    private UIElement Draw(OuroComposedNode n)
    {
        var p = n.Props;
        FrameworkElement drawn = n.Component switch
        {
            "Card" => new OuroCard { Density = OuroDensity.Small, Content = Column(16, n.Children) },
            "Stack" when n.Text("direction") == "horizontal" => Children(new OuroWrapPanel { Spacing = Gap(n.Text("gap")) }, n.Children),
            "Stack" => Children(new StackPanel { Spacing = Gap(n.Text("gap")) }, n.Children),
            "Heading" => Heading(n),
            "Text" => Words(n.Text("text"), n.Text("tone") == "muted"),
            "Image" => Picture(n),
            "Badge" => new OuroBadge { Text = n.Text("text"), Tone = Tone(n.Text("tone")) },
            "List" => List(n),
            "DescriptionList" => new OuroDescriptionList
            {
                Items = (p["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(t => new OuroDescriptionItem(OuroComposed.AsText(t["term"]), OuroComposed.AsText(t["detail"]))).ToArray(),
            },
            "Separator" => new OuroSeparator(),
            "Alert" => Alert(n),
            "TextField" => TextField(n),
            "Checkbox" => Checkbox(n),
            "Select" => Select(n),
            _ => Button(n),
        };
        // What UI Automation reads: a Select's ComboBox, not the panel around it.
        FrameworkElement spoken = drawn is OuroComposedSelect select ? select.Box : drawn;
        if (n.Component == "Card") AutomationProperties.SetName(drawn, FirstHeading(n) ?? "");
        AutomationProperties.SetAutomationId(spoken, "composed." + n.Id);
        if (p["@label"] is JsonValue label && drawn is not OuroTextField) AutomationProperties.SetName(spoken, OuroComposed.AsText(label));
        return drawn;
    }

    // Colours what the kit's own controls don't: once the element is in the tree (so it knows whether it sits on a card),
    // and again whenever the theme, a contrast theme or the element's mode changes.
    private void Paint(FrameworkElement e, Action paint)
    {
        e.Loaded += (_, _) => paint();
        paints.Add(paint);
    }

    private StackPanel Column(double spacing, IEnumerable<OuroComposedNode> children) => Children(new StackPanel { Spacing = spacing }, children);

    private T Children<T>(T panel, IEnumerable<OuroComposedNode> children) where T : Panel
    {
        foreach (var child in children) panel.Children.Add(Draw(child));
        return panel;
    }

    private TextBlock Heading(OuroComposedNode n)
    {
        var level = n.Props["level"] is JsonValue v && v.TryGetValue<int>(out var l) ? l : 2;
        var text = new TextBlock { Text = n.Text("text"), TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = level switch { 1 => 24, 3 => 16, _ => 20 } };
        AutomationProperties.SetHeadingLevel(text, level switch { 1 => AutomationHeadingLevel.Level1, 3 => AutomationHeadingLevel.Level3, _ => AutomationHeadingLevel.Level2 });
        Paint(text, () => text.Foreground = OuroUI.Brush(OuroUI.SurfacePaint(text).Text));
        return text;
    }

    private TextBlock Words(string words, bool muted)
    {
        var text = new TextBlock { Text = words, TextWrapping = TextWrapping.Wrap };
        Paint(text, () =>
        {
            var paint = OuroUI.SurfacePaint(text);
            text.Foreground = OuroUI.Brush(muted ? paint.Muted : paint.Text);
        });
        return text;
    }

    private OuroComposedImage Picture(OuroComposedNode n)
    {
        var image = new OuroComposedImage(n.Text("url"), n.Text("description"), n.Text("aspect") switch { "square" => 1, "portrait" => 3.0 / 4, "wide" => 21.0 / 9, _ => 4.0 / 3 });
        Paint(image, image.Paint);
        return image;
    }

    private OuroComposedList List(OuroComposedNode n)
    {
        var ordered = n.Props["ordered"] is JsonValue o && o.TryGetValue<bool>(out var b) && b;
        var list = new OuroComposedList((n.Props["items"] as JsonArray ?? new JsonArray()).Select(OuroComposed.AsText).ToArray(), ordered);
        Paint(list, list.Paint);
        return list;
    }

    private OuroComposedAlert Alert(OuroComposedNode n)
    {
        var alert = new OuroComposedAlert(n.Text("title"), n.Text("text"), Tone(n.Text("tone") is { Length: > 0 } t ? t : "info"));
        Paint(alert, alert.Paint);
        return alert;
    }

    private OuroTextField TextField(OuroComposedNode n)
    {
        var path = n.Text("value");
        var number = n.Text("type") == "number";
        var field = new OuroTextField
        {
            Label = n.Text("label"),
            Description = n.Text("hint"),
            Type = number ? OuroTextFieldType.Number : OuroTextFieldType.Text,
            IsMultiline = n.Props["multiline"] is JsonValue m && m.TryGetValue<bool>(out var multiline) && multiline,
            Text = OuroComposed.AsText(OuroPointer.Get(Data, path)),
        };
        field.TextChanged += (_, text) => Write(path, number ? Number(text) : JsonValue.Create(text));
        return field;
    }

    // A number field writes a number, and nothing (so the press sends null) when what the person typed isn't one.
    private static JsonNode? Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var d) || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out d)
            ? JsonValue.Create(d)
            : null;

    private OuroCheckbox Checkbox(OuroComposedNode n)
    {
        var path = n.Text("value");
        var box = new OuroCheckbox
        {
            Label = n.Text("label"),
            Description = n.Text("hint"),
            IsChecked = OuroPointer.Get(Data, path) is JsonValue v && v.TryGetValue<bool>(out var on) && on,
        };
        box.Checked += (_, _) => Write(path, JsonValue.Create(true));
        box.Unchecked += (_, _) => Write(path, JsonValue.Create(false));
        return box;
    }

    private OuroComposedSelect Select(OuroComposedNode n)
    {
        var path = n.Text("value");
        var options = (n.Props["options"] as JsonArray ?? new JsonArray()).Select(OuroComposed.AsText).ToArray();
        var select = new OuroComposedSelect(n.Text("label"), n.Text("hint"), options, OuroComposed.AsText(OuroPointer.Get(Data, path)));
        select.Chosen += (_, option) => Write(path, JsonValue.Create(option));
        Paint(select, select.Paint);
        return select;
    }

    private void Write(string path, JsonNode? value)
    {
        if (OuroPointer.Set(Data, path, value) is not JsonObject data) return;
        Data = data;
        DataChanged?.Invoke(this, data);
    }

    private OuroButton Button(OuroComposedNode n)
    {
        var primary = n.Text("emphasis") == "primary";
        var button = new OuroButton
        {
            Label = n.Text("label"),
            Tone = primary ? OuroTone.Primary : OuroTone.Secondary,
            Variant = primary ? OuroVariant.Solid : OuroVariant.Soft,
        };
        var press = n.Text("press");
        AutomationProperties.SetHelpText(button, press == "handoff" ? n.Text("handoff") : press == "confirm" ? OuroComposed.AsText(n.Props["confirm"]?["description"]) : "");
        button.Click += async (_, _) => await Pressed(n, button);
        return button;
    }

    private async Task Pressed(OuroComposedNode n, OuroButton button)
    {
        switch (n.Text("press"))
        {
            case "handoff":
                OnHandoff?.Invoke(new OuroComposedHandoff(n.Text("action"), n.Text("handoff")));
                return;
            case "confirm":
                if (!await Confirm(OuroComposed.ResolveButton(n, Data))) return;
                await Run(OuroComposed.PersonPress(n, true, Data), n, button);
                return;
            default:
                await Run(OuroComposed.PersonPress(n, false, Data), n, button);
                return;
        }
    }

    private async Task Run(OuroPersonPress press, OuroComposedNode n, OuroButton button)
    {
        if (OnPress is not { } run || button.IsBusy) return;
        button.IsBusy = true;
        try
        {
            await run(press, n);
        }
        finally
        {
            button.IsBusy = false;
        }
    }

    /// <summary>
    /// The confirm sheet: the action's label as the title, its description, and each prepared value under its label with
    /// the id beside a name. Cancel is the default button, so Enter never agrees by accident.
    /// </summary>
    private async Task<bool> Confirm(OuroComposedNode resolved)
    {
        if (confirming || XamlRoot is null) return false;
        var sheet = resolved.Props["confirm"] as JsonObject;
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = OuroComposed.AsText(sheet?["description"]), TextWrapping = TextWrapping.Wrap });
        var i = 0;
        foreach (var row in (sheet?["fields"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            var line = new OuroConfirmRow(OuroComposed.AsText(row["label"]), OuroComposed.AsText(row["value"]), row["raw"] is JsonNode raw ? OuroComposed.AsText(raw) : null);
            AutomationProperties.SetAutomationId(line, $"confirm.row.{i++}");
            body.Children.Add(line);
        }
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"{OuroComposed.AsText(sheet?["title"])}?",
            Content = body,
            PrimaryButtonText = resolved.Text("label"),
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        confirming = true;
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            confirming = false;
        }
    }
}

/// <summary>The surface to UI Automation: a Group named by the tree's first heading.</summary>
public sealed partial class OuroComposedSurfaceAutomationPeer(OuroComposedSurface owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(OuroComposedSurface);
}
