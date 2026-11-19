// Ouro's composed tree drawn with Ouro HX on Windows: web's and Apple's ComposedSurface. An agent composes the tree (in
// an MCP tool result's structuredContent.ouro, read with OuroComposedResult); the surface draws it only once it validates
// against the app's catalog (OuroComposed.ValidateTree), so every button is one of the app's declared actions with its
// consent class. Read and write buttons press at once; a consequential one shows what it will send in a dialog and
// presses only when the person confirms; a person-only one hands the person to the app's own screen and never presses.
// Fields write the person's answers into the data model, and a button bound to one sends what they entered.
//
// An agent changes a card it already showed with an update (OuroComposed.ApplyTreeUpdate): nodes replaced by id, data set
// by JSON pointer, all or nothing. The person's answers stay, except where the agent changed the value or its list item.
// A tree or an update that doesn't pass the checks shows one short alert; a refused update keeps the last card, hidden,
// with what the person entered, so a corrected update applies to it.
//
// A card can also come as A2UI messages (an MCP tool result's structuredContent.a2ui): ShowA2UI resolves them with core's
// A2UI adapter (OuroA2UI) into the same tree, which passes the same checks and draws the same way. A stream that fails
// them shows the same short alert, and its problems, in A2UI's error format, go back to the app for the agent.
//
// The surface is the person's: it has no way for an agent to press. Agents act through the action's own door (App
// Actions, the MCP server). Decisions: core docs/decisions/2026-11-14-composed-ui-own-tree.md,
// 2026-11-17-composed-tree-leads.md and 2026-11-18-composed-tree-updates.md.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
    /// <summary>The short alert a tree or an update that fails the checks shows, as web's views do.</summary>
    public const string ProblemTitle = "This card can't be shown", ProblemText = "It didn't pass the checks for composed UI.";

    private readonly List<Action> paints = [];
    private readonly StackPanel root = new() { Spacing = 16 };
    private OuroComposedAlert? alert;
    private FrameworkElement? body;
    private JsonNode? sent;
    private HashSet<string> edited = [];
    private bool confirming;
    private (string Surface, int Created)? origin;

    public OuroComposedSurface()
    {
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Content = root;
        OuroUI.Follow(this, Repaint);
    }

    /// <summary>The catalog the tree was checked against.</summary>
    public OuroComposedCatalog? Catalog { get; private set; }

    /// <summary>The tree drawn now, or null when nothing is.</summary>
    public OuroComposedNode? Tree { get; private set; }

    /// <summary>The data model: what the tree's fields start from and write the person's answers to.</summary>
    public JsonObject Data { get; private set; } = new();

    /// <summary>Why the last tree or update wasn't drawn; empty when it was.</summary>
    public IReadOnlyList<OuroTreeProblem> Problems { get; private set; } = [];

    /// <summary>
    /// Runs a pressed read or write button, and a consequential one once the person confirms. The press carries the
    /// action, the input with each bound field's answer read now, and that the person pressed it. The button is busy
    /// until the task ends.
    /// </summary>
    public Func<OuroPersonPress, OuroComposedNode, Task>? OnPress { get; set; }

    /// <summary>A person-only button: take the person to the action's own screen. It never presses.</summary>
    public Action<OuroComposedHandoff>? OnHandoff { get; set; }

    /// <summary>
    /// For a card that came as A2UI messages (<see cref="ShowA2UI"/>): receives each press as A2UI's <c>action</c> message
    /// (<see cref="OuroA2UI.ActionMessage(string, string, OuroPersonPress, string)"/>), with the pressed button, in place
    /// of <see cref="OnPress"/>. The button is busy until the task ends.
    /// </summary>
    public Func<JsonObject, OuroComposedNode, Task>? OnA2UIAction { get; set; }

    /// <summary>The A2UI surface drawn now, when the card came as A2UI messages; null otherwise.</summary>
    public string? A2UISurfaceId { get; private set; }

    /// <summary>A field wrote the person's answer into the data model, or an update changed it.</summary>
    public event EventHandler<JsonObject>? DataChanged;

    /// <summary>
    /// Draws a tool result: a whole tree, or an update to the tree drawn now (<see cref="OuroComposedResult.Update"/>). A
    /// tree that fails the checks shows the short alert in place of a card; an update that fails them shows it over the
    /// last card, which stays hidden with what the person entered. Returns the problems, empty when it drew.
    /// </summary>
    public IReadOnlyList<OuroTreeProblem> Show(OuroComposedCatalog catalog, OuroComposedResult result)
    {
        var problems = result.Problems(catalog);
        if (result.Update is not null) return problems.Count > 0 ? Hold(problems) : Update(result.Update);
        return problems.Count > 0 ? Refuse(problems) : Show(catalog, result.Tree, result.Data);
    }

    /// <summary>Draws a tree when it validates against the catalog; otherwise shows the short alert and returns why.</summary>
    public IReadOnlyList<OuroTreeProblem> Show(OuroComposedCatalog catalog, JsonNode? tree, JsonObject? data = null)
    {
        var problems = OuroComposed.ValidateTree(catalog, tree);
        if (problems.Count > 0) return Refuse(problems);
        A2UISurfaceId = null;
        origin = null;
        Catalog = catalog;
        sent = data?.DeepClone();
        Data = (JsonObject?)data?.DeepClone() ?? new JsonObject();
        edited = [];
        Render(OuroComposedNode.FromJson(tree));
        return Problems;
    }

    /// <summary>
    /// Applies an agent's update, <c>{ nodes?, data? }</c>, to the tree drawn now (<see cref="OuroComposed.ApplyTreeUpdate"/>):
    /// all of it, or none of it and the short alert over the last card. What the person entered stays, except where the
    /// update changed the value or the list item it sits in. Returns the problems, empty when it applied.
    /// </summary>
    public IReadOnlyList<OuroTreeProblem> Update(JsonNode? update)
    {
        if (Catalog is null || Tree is null)
            return Refuse([new("/update", "An update changes the composed tree the view shows, and it shows none yet; send the whole tree first, in structuredContent.ouro.tree.")]);
        var applied = OuroComposed.ApplyTreeUpdate(Catalog, Tree.ToJson(), sent, update);
        if (!applied.Applied) return Hold(applied.Problems);
        (Data, var kept) = OuroComposed.KeepAnswers(sent, applied.Data, Data, edited);
        edited = [.. kept];
        sent = applied.Data?.DeepClone();
        Render(OuroComposedNode.FromJson(applied.Tree));
        DataChanged?.Invoke(this, Data);
        return Problems;
    }

    /// <summary>
    /// Draws a card that came as A2UI messages, such as a tool result's <c>structuredContent.a2ui.messages</c>
    /// (<see cref="OuroA2UI.TryReadToolResult"/>): core's A2UI adapter resolves them into Ouro's composed tree and data
    /// model (<see cref="OuroA2UI.ResolveMessages"/>), and the tree must pass the same checks as one sent itself. It shows
    /// <paramref name="surfaceId"/>, or the first surface the messages leave. A stream that fails the checks, or that
    /// leaves no such surface, shows the short alert in place of a card. Returns the problems, in A2UI's error format
    /// for the agent, empty when it drew (or the surface has no root, so there is nothing to draw).
    /// </summary>
    /// <remarks>
    /// Show the stream again as it grows: while the surface is the one the same <c>createSurface</c> made, what the person
    /// entered stays, except where the agent changed the value or the list item it sits in, as with an update.
    /// <paramref name="label"/> is the app's own name for an id a confirm sheet shows (<see cref="OuroComposed.ActionButtonProps"/>).
    /// </remarks>
    public IReadOnlyList<OuroA2UIProblem> ShowA2UI(OuroComposedCatalog catalog, JsonNode? messages, string? surfaceId = null, Func<OuroLabelContext, string?>? label = null)
    {
        var resolved = OuroA2UI.ResolveMessages(catalog, messages, label);
        if (!resolved.Valid)
        {
            Refuse(resolved.Problems.Select(p => p.ToTreeProblem()).ToArray());
            return resolved.Problems;
        }
        var surface = surfaceId is null ? resolved.Surfaces.FirstOrDefault() : resolved.Surfaces.FirstOrDefault(s => s.SurfaceId == surfaceId);
        if (surface is null)
        {
            OuroA2UIProblem missing = surfaceId is null
                ? new("VALIDATION_FAILED", "", "The messages leave no surface to show; create one with createSurface.")
                : new("VALIDATION_FAILED", "", $"No surface {surfaceId} in the messages; they leave {(resolved.Surfaces.Count > 0 ? string.Join(", ", resolved.Surfaces.Select(s => s.SurfaceId)) : "none")}.", surfaceId);
            Refuse([missing.ToTreeProblem()]);
            return [missing];
        }
        if (surface.Tree is null)
        {
            // No component with id root yet: nothing to draw, and nothing wrong.
            Clear();
            A2UISurfaceId = surface.SurfaceId;
            return [];
        }
        var data = surface.Data as JsonObject;
        // Which createSurface made it: the same surface shown again keeps the person's answers; one made again starts over.
        var created = ((JsonArray)messages!).Select((m, i) => (m, i)).Last(x => x.m is JsonObject o && o["createSurface"] is JsonObject c && c["surfaceId"] is JsonValue id && id.GetValueKind() == JsonValueKind.String && id.GetValue<string>() == surface.SurfaceId).i;
        var problems = OuroComposed.ValidateTree(catalog, surface.Tree);
        if (problems.Count > 0)
        {
            Refuse(problems);
            return problems.Select(p => new OuroA2UIProblem("VALIDATION_FAILED", p.Path, p.Message, surface.SurfaceId, null, p.Refusal)).ToArray();
        }
        if (origin is { } was && was.Surface == surface.SurfaceId && was.Created == created && ReferenceEquals(Catalog, catalog) && Tree is not null)
        {
            // The same surface again: keep what the person entered, as an update does.
            (Data, var kept) = OuroComposed.KeepAnswers(sent, data, Data, edited);
            edited = [.. kept];
            sent = data?.DeepClone();
            Render(OuroComposedNode.FromJson(surface.Tree));
            DataChanged?.Invoke(this, Data);
        }
        else
        {
            Show(catalog, surface.Tree, data);
        }
        A2UISurfaceId = surface.SurfaceId;
        origin = (surface.SurfaceId, created);
        return [];
    }

    // A tree to draw: the alert goes, and what had keyboard focus in the last card has it again in this one.
    private void Render(OuroComposedNode tree)
    {
        var focused = XamlRoot is { } xamlRoot && FocusManager.GetFocusedElement(xamlRoot) is FrameworkElement f ? AutomationProperties.GetAutomationId(f) : "";
        Tree = tree;
        Problems = [];
        paints.Clear();
        body = Draw(tree);
        root.Children.Clear();
        root.Children.Add(body);
        alert = null;
        AutomationProperties.SetName(this, FirstHeading(tree) ?? "");
        if (focused.StartsWith("composed.", StringComparison.Ordinal) && Find(body, focused) is { } again)
        {
            // Once, for this redraw: the element loading again later (a page navigated back to) takes no focus.
            RoutedEventHandler? once = null;
            once = (_, _) =>
            {
                again.Loaded -= once;
                _ = again is OuroTextField field ? field.FocusInput(FocusState.Programmatic) : again is Control control && control.Focus(FocusState.Programmatic);
            };
            again.Loaded += once;
        }
        if (IsLoaded) Repaint();
    }

    // Nothing to draw: the alert alone.
    private IReadOnlyList<OuroTreeProblem> Refuse(IReadOnlyList<OuroTreeProblem> problems)
    {
        Clear();
        return Alert(problems);
    }

    // Nothing drawn, and no alert.
    private void Clear()
    {
        A2UISurfaceId = null;
        origin = null;
        Catalog = null;
        Tree = null;
        sent = null;
        Data = new JsonObject();
        edited = [];
        paints.Clear();
        body = null;
        root.Children.Clear();
        alert = null;
        Problems = [];
        AutomationProperties.SetName(this, "");
    }

    // An update that can't apply: the alert over the last card, which stays as the person left it, hidden.
    private IReadOnlyList<OuroTreeProblem> Hold(IReadOnlyList<OuroTreeProblem> problems)
    {
        if (body is not null) body.Visibility = Visibility.Collapsed;
        return Alert(problems);
    }

    private IReadOnlyList<OuroTreeProblem> Alert(IReadOnlyList<OuroTreeProblem> problems)
    {
        Problems = problems;
        if (alert is null)
        {
            alert = new OuroComposedAlert(ProblemTitle, ProblemText, OuroTone.Destructive);
            AutomationProperties.SetAutomationId(alert, "composed.problem");
            alert.Loaded += (_, _) => alert?.Paint();
            root.Children.Insert(0, alert);
        }
        if (IsLoaded) alert.Paint();
        return problems;
    }

    // The drawn element with this automation id, for focus: a Select's is its ComboBox, inside the panel Draw gave.
    private static FrameworkElement? Find(DependencyObject e, string id)
    {
        if (e is FrameworkElement f && AutomationProperties.GetAutomationId(f) == id) return f;
        IEnumerable<DependencyObject> children = e switch
        {
            Panel p => p.Children,
            ContentControl { Content: DependencyObject c } => [c],
            Border { Child: { } b } => [b],
            _ => [],
        };
        return children.Select(c => Find(c, id)).FirstOrDefault(x => x is not null);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroComposedSurfaceAutomationPeer(this);

    private void Repaint()
    {
        foreach (var paint in paints) paint();
        alert?.Paint();
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

    private FrameworkElement Draw(OuroComposedNode n)
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
        // Read as the validator reads it: 1.0 is level 1.
        var level = OuroComposed.AsText(n.Props["level"]) switch { "1" => 1, "3" => 3, _ => 2 };
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

    // A number field writes a number, and nothing (so the press sends null) when what the person typed isn't a finite one:
    // NaN and infinity have no JSON.
    private static JsonNode? Number(string text) =>
        (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var d) || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) && double.IsFinite(d)
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
        edited.Add(path);
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
                // The press sends exactly what the sheet showed, and nothing if the app drew another tree meanwhile.
                var shown = Tree;
                var resolved = OuroComposed.ResolveButton(n, Data);
                if (!await Confirm(resolved) || !ReferenceEquals(shown, Tree)) return;
                await Run(OuroComposed.PersonPress(resolved, true), n, button);
                return;
            default:
                await Run(OuroComposed.PersonPress(n, false, Data), n, button);
                return;
        }
    }

    private async Task Run(OuroPersonPress press, OuroComposedNode n, OuroButton button)
    {
        Func<Task>? run = A2UISurfaceId is { } surfaceId && OnA2UIAction is { } send
            ? () => send(OuroA2UI.ActionMessage(surfaceId, n.Id, press, OuroA2UI.Timestamp(DateTimeOffset.UtcNow)), n)
            : OnPress is { } pressed ? () => pressed(press, n) : null;
        if (run is null || button.IsBusy) return;
        button.IsBusy = true;
        try
        {
            await run();
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
