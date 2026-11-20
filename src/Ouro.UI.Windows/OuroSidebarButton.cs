// The pressable part of Core's Sidebar (vendor/core/components/sidebar.spec.json): one row, drawn as a templated WinUI Button
// so it keeps WinUI's Enter and Space keys, focus ring and press tracking. Use OuroSidebar, which builds and wires these.
// UI Automation sees a ListItem named by its label and then its badge, with SelectionItem (the current page is selected), and
// ExpandCollapse on a group. A row takes the muted wash while pressed instead of scaling, as the spec says.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Shapes = Microsoft.UI.Xaml.Shapes;

namespace Ouro.UI.Windows;

/// <summary>One row of a sidebar: an optional icon, the words, an optional badge, and a chevron on a group.</summary>
public sealed partial class OuroSidebarButton : Button
{
    /// <summary>Core's metrics, in effective pixels: the touch height, the bar at the start edge of the current row, and the icon.</summary>
    public const double MinTarget = 44, BarThickness = 3, IconSize = 20;

    private readonly TextBlock label = new() { TextWrapping = TextWrapping.Wrap, FontSize = 14, IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None, VerticalAlignment = VerticalAlignment.Center };
    private readonly FontIcon icon = new() { FontSize = IconSize, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"), IsTextScaleFactorEnabled = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock badgeText = new() { FontSize = 12, IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None };
    private readonly Border badgeBox = new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 1, 8, 1), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly FontIcon chevron = new() { FontSize = 12, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"), IsTextScaleFactorEnabled = false, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private Shapes.Rectangle? bar;
    private bool current, collapsed, group, expanded, hover;
    private string text = "", badge = "";

    public OuroSidebarButton()
    {
        DefaultStyleKey = typeof(OuroSidebarButton);
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(chevron, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(badgeBox, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(label, AccessibilityView.Raw);
        badgeBox.Child = badgeText;
        var row = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Auto } } };
        Grid.SetColumn(label, 1);
        Grid.SetColumn(badgeBox, 2);
        Grid.SetColumn(chevron, 3);
        row.Children.Add(icon);
        row.Children.Add(label);
        row.Children.Add(badgeBox);
        row.Children.Add(chevron);
        Content = row;
        OuroUI.Follow(this, Refresh);
        RegisterPropertyChangedCallback(IsPressedProperty, (_, _) => Refresh());
        PointerEntered += (_, _) => { hover = true; Refresh(); };
        PointerExited += (_, _) => { hover = false; Refresh(); };
        SizeChanged += (_, _) => Refresh();
        label.SizeChanged += (_, _) => Refresh();
    }

    /// <summary>The words, which are also the start of its spoken name.</summary>
    public string Label
    {
        get => text;
        set { text = value; label.Text = value; Refresh(); }
    }

    /// <summary>The icon, as a glyph of Segoe Fluent Icons; empty for none. It is decorative: the label names the row.</summary>
    public string Glyph
    {
        get => icon.Glyph;
        set { icon.Glyph = value; icon.Visibility = value.Length == 0 ? Visibility.Collapsed : Visibility.Visible; }
    }

    /// <summary>A short count or mark read after the label, such as "12"; empty for none.</summary>
    public string Badge
    {
        get => badge;
        set { badge = value; badgeText.Text = value; Refresh(); }
    }

    /// <summary>Whether this is the current page: accent words on an accent wash, with a bar at the start edge.</summary>
    public bool IsCurrent
    {
        get => current;
        set
        {
            if (current == value) return;
            current = value;
            (FrameworkElementAutomationPeer.FromElement(this) as OuroSidebarButtonAutomationPeer)?.RaiseSelected(!value, value);
            Refresh();
        }
    }

    /// <summary>Whether the sidebar is a rail of icons, so only the icon shows.</summary>
    public bool IsCollapsed
    {
        get => collapsed;
        set { collapsed = value; Refresh(); }
    }

    /// <summary>Whether this row is a group that shows and hides nested rows.</summary>
    public bool IsGroup
    {
        get => group;
        set { group = value; Refresh(); }
    }

    /// <summary>Whether a group shows its nested rows.</summary>
    public bool IsExpanded
    {
        get => expanded;
        set
        {
            if (expanded == value) return;
            expanded = value;
            (FrameworkElementAutomationPeer.FromElement(this) as OuroSidebarButtonAutomationPeer)?.RaiseExpanded(!value, value);
            Refresh();
            ExpandedChanged?.Invoke();
        }
    }

    /// <summary>The words and then the badge, or the words alone on the rail.</summary>
    internal string SpokenName => OuroNav.SpokenName(text, badge, collapsed);

    /// <summary>The sidebar this row belongs to, which names it to UI Automation.</summary>
    internal FrameworkElement? Container { get; set; }

    /// <summary>Tells the sidebar to show or hide the rows under this group.</summary>
    internal Action? ExpandedChanged { get; set; }

    /// <summary>What UI Automation's Select and Invoke ask for: the sidebar reports the press.</summary>
    internal Action? Choose { get; set; }

    /// <summary>The row's words are cut off.</summary>
    internal bool Trimmed => label.IsTextTrimmed;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        bar = GetTemplateChild("Bar") as Shapes.Rectangle;
        Refresh();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroSidebarButtonAutomationPeer(this);

    private void Refresh()
    {
        var p = OuroUI.SurfacePaint(this);
        var wash = current ? OuroNav.Wash(p.Behind, p.Checked) : 0u;
        var pressedOrHover = IsPressed ? OuroPaint.Mix(p.Behind, p.Text, 0.14) : hover ? OuroPaint.Mix(p.Behind, p.Text, 0.08) : 0u;
        var fill = current ? wash : pressedOrHover;
        var behind = fill == 0 ? p.Behind : fill;
        var words = current ? OuroNav.CurrentWords(p.Checked, wash, p.Text) : p.Text;
        var badgeFill = OuroPaint.Mix(behind, p.Text, 0.12);
        Background = OuroUI.Brush(fill);
        Foreground = OuroUI.Brush(words);
        label.Foreground = Foreground;
        icon.Foreground = Foreground;
        chevron.Foreground = Foreground;
        badgeText.Foreground = Foreground;
        badgeBox.Background = OuroUI.Brush(badgeFill);
        label.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        badgeBox.Visibility = collapsed || badge.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        chevron.Visibility = collapsed || !group ? Visibility.Collapsed : Visibility.Visible;
        chevron.Glyph = expanded ? "\uE70E" : "\uE70D";
        ((Grid)Content).ColumnSpacing = collapsed ? 0 : 12;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(collapsed ? 12 : 16, 8, 16, 8);
        CornerRadius = new CornerRadius(8);
        MinHeight = MinTarget;
        MinWidth = MinTarget;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Center;
        if (bar is not null)
        {
            bar.Visibility = current ? Visibility.Visible : Visibility.Collapsed;
            bar.Fill = OuroUI.Brush(p.Checked);
            bar.Width = BarThickness;
        }
        OuroUI.Report(this, "", "sidebar",
            ("label", OuroPaint.Hex(words)), ("behind", OuroPaint.Hex(behind)), ("section", OuroPaint.Hex(p.Muted)), ("plain", OuroPaint.Hex(p.Behind)), ("current", current), ("collapsed", collapsed), ("group", group),
            ("badge", badge.Length > 0 && !collapsed), ("badgeText", OuroPaint.Hex(words)), ("badgeFill", OuroPaint.Hex(badgeFill)), ("spokenName", SpokenName.Replace(";", "").Replace("=", "")),
            ("list", Container is null ? "" : AutomationProperties.GetAutomationId(Container)), ("trimmed", label.IsTextTrimmed), ("textHeight", Math.Round(label.ActualHeight)));
    }
}

/// <summary>A sidebar row to UI Automation: a ListItem named by its label and badge, selected while it is the current page.</summary>
public sealed partial class OuroSidebarButtonAutomationPeer(OuroSidebarButton owner) : ButtonAutomationPeer(owner), ISelectionItemProvider, IExpandCollapseProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

    protected override string GetClassNameCore() => nameof(OuroSidebarButton);

    protected override string GetNameCore() => owner.SpokenName;

    protected override string GetAutomationIdCore() => owner.Container is { } c ? $"{AutomationProperties.GetAutomationId(c)}.{owner.Label}" : base.GetAutomationIdCore();

    protected override object GetPatternCore(PatternInterface patternInterface) => patternInterface switch
    {
        PatternInterface.SelectionItem => this,
        PatternInterface.ExpandCollapse when owner.IsGroup => this,
        PatternInterface.ExpandCollapse => null!,
        _ => base.GetPatternCore(patternInterface),
    };

    public bool IsSelected => owner.IsCurrent;

    public IRawElementProviderSimple SelectionContainer => ProviderFromPeer(CreatePeerForElement(owner.Container ?? owner));

    public void Select() => owner.Choose?.Invoke();

    public void AddToSelection() => Select();

    public void RemoveFromSelection()
    {
    }

    public ExpandCollapseState ExpandCollapseState => owner.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public void Expand() => owner.IsExpanded = true;

    public void Collapse() => owner.IsExpanded = false;

    internal void RaiseSelected(bool was, bool now) => RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, was, now);

    internal void RaiseExpanded(bool was, bool now) => RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty, was ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed, now ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
}
