// Core's ItemGroup (vendor/core/components/item-group.spec.json): a labelled list of Items, with a line between the rows, an optional
// numbering and a message when it has none. UI Automation sees a List named by its label, each row a ListItem; the Items keep their own
// Invoke pattern. With no rows and an empty message, the message replaces the list and is a Text. The group is not a tab stop; each
// pressable row is its own, in order.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Ouro.UI.Windows;

/// <summary>A labelled list of Items, with a line between the rows, an optional numbering and a message when it has none.</summary>
public sealed partial class OuroItemGroup : Control
{
    /// <summary>Core's metrics, in effective pixels.</summary>
    public const double DividerWidth = 1, OutlineRadius = 12, EmptyPadding = 24;

    private readonly TextBlock heading = new() { TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None };
    private readonly StackPanel rows = new();
    private readonly Border frame = new();
    private readonly List<OuroItemGroupRow> wrapped = [];
    private StackPanel? root;
    private IReadOnlyList<FrameworkElement> items = [];
    private string label = "", empty = "";
    private OuroItemGroupVariant variant = OuroItemGroupVariant.Divided;
    private bool ordered;

    public OuroItemGroup()
    {
        DefaultStyleKey = typeof(OuroItemGroup);
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetAccessibilityView(heading, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(message, AccessibilityView.Raw);
        frame.Child = rows;
        OuroUI.Follow(this, Refresh);
        SizeChanged += (_, _) => Refresh();
    }

    /// <summary>A short label above the list that also names it, such as "Recipes".</summary>
    public string Label
    {
        get => label;
        set { label = value; Refresh(); }
    }

    /// <summary>Whether a line is drawn between rows (divided, the default), none (plain), or the whole list sits in one bordered box (outline).</summary>
    public OuroItemGroupVariant Variant
    {
        get => variant;
        set { variant = value; Refresh(); }
    }

    /// <summary>Whether the order matters: a screen reader says each row's place, such as "2 of 5".</summary>
    public bool Ordered
    {
        get => ordered;
        set { ordered = value; Build(); }
    }

    /// <summary>What to show in place of the list when there are no rows, such as "No recipes yet".</summary>
    public string Empty
    {
        get => empty;
        set { empty = value; Refresh(); }
    }

    /// <summary>The rows, in order. Each is one list item to a screen reader; an <see cref="OuroItem"/> is named by its title.</summary>
    public IReadOnlyList<FrameworkElement> Items
    {
        get => items;
        set { items = value; Build(); }
    }

    /// <summary>Whether the empty message is shown instead of the list.</summary>
    internal bool ShowsEmpty => items.Count == 0 && empty.Length > 0;

    /// <summary>The words a screen reader reads for this group: the label, or the empty message when it replaces the list.</summary>
    internal string SpokenName => ShowsEmpty ? empty : label;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        root = GetTemplateChild("Root") as StackPanel;
        Build();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroItemGroupAutomationPeer(this);

    private static bool Holds(DependencyObject? ancestor, DependencyObject? node)
    {
        for (; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, ancestor)) return true;
        return false;
    }

    private void Build()
    {
        if (root is null) return;
        // The row that holds focus now, so focus stays in the list through the rebuild: on the same control when it is still a row, else on
        // the row that took its place.
        var focused = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as Control;
        var at = focused is null ? -1 : wrapped.FindIndex(r => Holds(r, focused));
        rows.Children.Clear();
        wrapped.Clear();
        foreach (var (item, i) in items.Select((item, i) => (item, i)))
        {
            if (item.Parent is Panel old) old.Children.Remove(item);
            var row = new OuroItemGroupRow { Item = item, Index = i, Count = items.Count, Ordered = ordered };
            row.Children.Add(item);
            wrapped.Add(row);
            rows.Children.Add(row);
        }
        root.Children.Clear();
        root.Children.Add(heading);
        root.Children.Add(frame);
        root.Children.Add(message);
        Refresh();
        if (at < 0) return;
        if (focused is not null && items.Any(it => Holds(it, focused))) focused.Focus(FocusState.Keyboard);
        else if (OuroListRules.FocusAfterRemoval(at, wrapped.Count) is var next and >= 0)
            (FocusManager.FindFirstFocusableElement(wrapped[next]) as Control)?.Focus(FocusState.Keyboard);
    }

    private void Refresh()
    {
        if (root is null) return;
        var p = OuroUI.SurfacePaint(this);
        var showsEmpty = ShowsEmpty;
        heading.Text = label;
        heading.Foreground = OuroUI.Brush(p.Text);
        heading.Visibility = label.Length > 0 && !showsEmpty ? Visibility.Visible : Visibility.Collapsed;
        heading.Margin = new Thickness(0, 0, 0, 8);
        message.Text = empty;
        message.Foreground = OuroUI.Brush(p.Muted);
        message.Padding = new Thickness(EmptyPadding);
        message.Visibility = showsEmpty ? Visibility.Visible : Visibility.Collapsed;
        frame.Visibility = showsEmpty ? Visibility.Collapsed : Visibility.Visible;
        frame.BorderBrush = OuroUI.Brush(p.Line);
        frame.BorderThickness = new Thickness(variant == OuroItemGroupVariant.Outline ? DividerWidth : 0);
        frame.CornerRadius = new CornerRadius(variant == OuroItemGroupVariant.Outline ? OutlineRadius : 0);
        foreach (var row in wrapped)
        {
            row.BorderBrush = OuroUI.Brush(p.Line);
            row.BorderThickness = new Thickness(0, OuroListRules.LineAbove(variant, row.Index) ? DividerWidth : 0, 0, 0);
        }
        var words = showsEmpty ? message : heading;
        OuroUI.Report(this, "", "item-group",
            ("label", OuroPaint.Hex(p.Text)), ("empty", OuroPaint.Hex(p.Muted)), ("line", OuroPaint.Hex(p.Line)), ("behind", OuroPaint.Hex(p.Behind)),
            ("variant", variant), ("ordered", ordered), ("items", items.Count), ("showsEmpty", showsEmpty),
            ("trimmed", words.IsTextTrimmed), ("textHeight", Math.Round(words.ActualHeight - words.Padding.Top - words.Padding.Bottom)));
    }
}

/// <summary>One row of an item group: its Item, with the line above it, a ListItem to UI Automation.</summary>
internal sealed partial class OuroItemGroupRow : Grid
{
    public FrameworkElement? Item { get; set; }

    public int Index { get; set; }

    public int Count { get; set; }

    public bool Ordered { get; set; }

    /// <summary>The row's words: an <see cref="OuroItem"/>'s title, else the name the app gave the element.</summary>
    public string Words => Item is OuroItem i ? i.Title : Item is null ? "" : AutomationProperties.GetName(Item);

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroItemGroupRowAutomationPeer(this);
}

/// <summary>A row to UI Automation: a ListItem named by its words, and for an ordered list its place.</summary>
internal sealed partial class OuroItemGroupRowAutomationPeer(OuroItemGroupRow owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

    protected override string GetClassNameCore() => "OuroItemGroupRow";

    protected override string GetNameCore() => OuroListRules.RowName(owner.Words, owner.Index, owner.Count, owner.Ordered);

    protected override bool IsKeyboardFocusableCore() => false;
}

/// <summary>An item group to UI Automation: a List named by its label, or its empty message as a Text when it has no rows.</summary>
public sealed partial class OuroItemGroupAutomationPeer(OuroItemGroup owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => owner.ShowsEmpty ? AutomationControlType.Text : AutomationControlType.List;

    protected override string GetClassNameCore() => nameof(OuroItemGroup);

    protected override string GetNameCore() => owner.SpokenName;

    protected override bool IsKeyboardFocusableCore() => false;
}
