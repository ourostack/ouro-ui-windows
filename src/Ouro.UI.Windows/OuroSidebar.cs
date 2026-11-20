// Core's Sidebar (vendor/core/components/sidebar.spec.json): a column of a product's sections, each an optional heading over
// rows of an icon, words and an optional badge, with a header and footer, collapsible to a rail of icons. UI Automation sees a
// List named by its label, of ListItems (the current one selected), headings as Header items, a group as a ListItem with
// ExpandCollapse. Each row is its own tab stop. On Windows it follows NavigationView's meaning, with a compact pane as the rail.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Ouro.UI.Windows;

/// <summary>One row to offer. A row with <paramref name="Children"/> is a group that shows and hides them, one level deep.</summary>
public sealed record OuroSidebarItem(string Label, string Glyph = "", string Badge = "", IReadOnlyList<OuroSidebarItem>? Children = null);

/// <summary>A run of rows under an optional heading; leave <paramref name="Heading"/> empty for none.</summary>
public sealed record OuroSidebarSection(string Heading, IReadOnlyList<OuroSidebarItem> Items);

/// <summary>A section's heading: muted words that name the rows below it.</summary>
public sealed partial class OuroSidebarHeading : Grid
{
    /// <summary>The words.</summary>
    public string Label { get; set; } = "";

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroSidebarHeadingAutomationPeer(this);
}

/// <summary>A sidebar heading to UI Automation: a Header item named by its words.</summary>
public sealed partial class OuroSidebarHeadingAutomationPeer(OuroSidebarHeading owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Header;

    protected override string GetClassNameCore() => nameof(OuroSidebarHeading);

    protected override string GetNameCore() => owner.Label;
}

/// <summary>The column of navigation down the side of a window.</summary>
public sealed partial class OuroSidebar : Control
{
    private readonly StackPanel column = new() { Spacing = 4 };
    private readonly List<OuroSidebarButton> buttons = [];
    private readonly List<(OuroSidebarButton Group, List<OuroSidebarButton> Nested)> groups = [];
    private readonly List<(OuroSidebarHeading Heading, TextBlock Words)> headings = [];
    private readonly ContentPresenter header = new() { HorizontalContentAlignment = HorizontalAlignment.Left };
    private readonly ContentPresenter footer = new() { HorizontalContentAlignment = HorizontalAlignment.Left };
    private Grid? root;
    private IReadOnlyList<OuroSidebarSection> sections = [];
    private string current = "";
    private bool collapsed;

    public OuroSidebar()
    {
        DefaultStyleKey = typeof(OuroSidebar);
        IsTabStop = false;
        OuroUI.Follow(this, Paint);
        SizeChanged += (_, _) => { if (root is not null) root.Clip = new RectangleGeometry { Rect = new global::Windows.Foundation.Rect(0, 0, ActualWidth, ActualHeight) }; };
    }

    /// <summary>Names the navigation for a screen reader, such as "Main". It is not shown.</summary>
    public string Label { get; set; } = "";

    /// <summary>The sections, top to bottom.</summary>
    public IReadOnlyList<OuroSidebarSection> Sections
    {
        get => sections;
        set { sections = value; Build(); }
    }

    /// <summary>The label of the current page's row.</summary>
    public string Current
    {
        get => current;
        set { current = value; Apply(); }
    }

    /// <summary>Whether the sidebar is a rail of icons, 64 wide, instead of 256. The header and footer are clipped to it.</summary>
    public bool Collapsed
    {
        get => collapsed;
        set { collapsed = value; Apply(); CollapsedChange?.Invoke(this, value); }
    }

    /// <summary>Content above the sections, such as the product's name.</summary>
    public object? Header
    {
        get => header.Content;
        set => header.Content = value;
    }

    /// <summary>Content below the sections, such as the signed-in person.</summary>
    public object? Footer
    {
        get => footer.Content;
        set => footer.Content = value;
    }

    /// <summary>A row was pressed, with its label. The app navigates; set <see cref="Current"/> to match. A group's row only opens and closes.</summary>
    public event EventHandler<string>? Pressed;

    /// <summary>The sidebar was collapsed to the rail or opened again.</summary>
    public event EventHandler<bool>? CollapsedChange;

    /// <summary>Every row now, which the tests and the report read.</summary>
    internal IReadOnlyList<OuroSidebarButton> Buttons => buttons;

    internal OuroSidebarButton? CurrentButton => buttons.FirstOrDefault(b => b.IsCurrent);

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        root = GetTemplateChild("Root") as Grid;
        if (root is null) return;
        var stack = new StackPanel { Spacing = 12, Padding = new Thickness(8, 12, 8, 12) };
        stack.Children.Add(header);
        stack.Children.Add(column);
        stack.Children.Add(footer);
        root.Children.Clear();
        root.Children.Add(stack);
        Build();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroSidebarAutomationPeer(this);

    private void Build()
    {
        buttons.Clear();
        groups.Clear();
        headings.Clear();
        column.Children.Clear();
        foreach (var section in sections)
        {
            if (section.Heading.Length > 0)
            {
                var words = new TextBlock { Text = section.Heading, FontSize = 12, IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None, Margin = new Thickness(16, 8, 16, 4) };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(words, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
                var heading = new OuroSidebarHeading { Label = section.Heading };
                heading.Children.Add(words);
                headings.Add((heading, words));
                column.Children.Add(heading);
            }
            foreach (var item in section.Items) AddRow(item);
        }
        Apply();
    }

    private void AddRow(OuroSidebarItem item)
    {
        var row = Make(item);
        column.Children.Add(row);
        if (item.Children is not { Count: > 0 } kids) return;
        row.IsGroup = true;
        var nested = new List<OuroSidebarButton>();
        foreach (var kid in kids)
        {
            var b = Make(kid);
            b.Margin = new Thickness(16, 0, 0, 0);
            nested.Add(b);
            column.Children.Add(b);
        }
        groups.Add((row, nested));
        row.Click += (_, _) => { row.IsExpanded = !row.IsExpanded; Apply(); };
    }

    private OuroSidebarButton Make(OuroSidebarItem item)
    {
        var button = new OuroSidebarButton { Label = item.Label, Glyph = item.Glyph, Badge = item.Badge, Container = this };
        void Press() => Pressed?.Invoke(this, item.Label);
        if (item.Children is not { Count: > 0 })
        {
            button.Choose = Press;
            button.Click += (_, _) => Press();
        }
        buttons.Add(button);
        return button;
    }

    private void Apply()
    {
        Width = collapsed ? OuroNav.RailWidth : OuroNav.SidebarWidth;
        foreach (var b in buttons)
        {
            b.IsCurrent = b.Label == current;
            b.IsCollapsed = collapsed;
        }
        foreach (var (group, nested) in groups)
            foreach (var n in nested) n.Visibility = group.IsExpanded && !collapsed ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (heading, _) in headings) heading.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        Paint();
    }

    private void Paint()
    {
        var p = OuroUI.SurfacePaint(this);
        foreach (var (_, words) in headings) words.Foreground = OuroUI.Brush(p.Muted);
    }
}

/// <summary>A sidebar to UI Automation: a List named by its label, whose Selection pattern says which row is current.</summary>
public sealed partial class OuroSidebarAutomationPeer(OuroSidebar owner) : FrameworkElementAutomationPeer(owner), ISelectionProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;

    protected override string GetClassNameCore() => nameof(OuroSidebar);

    protected override string GetNameCore() => owner.Label;

    protected override bool IsKeyboardFocusableCore() => false;

    protected override object GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Selection ? this : base.GetPatternCore(patternInterface);

    public bool CanSelectMultiple => false;

    public bool IsSelectionRequired => false;

    public IRawElementProviderSimple[] GetSelection() =>
        owner.CurrentButton is { } b ? [ProviderFromPeer(CreatePeerForElement(b))] : [];
}
