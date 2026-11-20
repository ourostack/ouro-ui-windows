// Core's TabBar (vendor/core/components/tab-bar.spec.json): two to five of an app's top-level pages, each an icon over a short
// label, the current one on an accent wash, and one optional accessory (such as Search) in a circle of its own at the end.
// UI Automation sees a List named by its label, of ListItems named by their labels with SelectionItem. Each item is its own
// tab stop. A bottom bar is a phone pattern; on a desktop use a sidebar.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace Ouro.UI.Windows;

/// <summary>One tab to offer: the words under it and its icon, a glyph of Segoe Fluent Icons.</summary>
public sealed record OuroTabBarItem(string Label, string Glyph);

/// <summary>The bar of tabs at the bottom of a phone-sized window.</summary>
public sealed partial class OuroTabBar : Control
{
    private readonly Grid grid = new() { MinHeight = OuroListButton.BarHeight };
    private readonly List<OuroListButton> buttons = [];
    private Grid? root;
    private IReadOnlyList<OuroTabBarItem> items = [];
    private OuroTabBarItem? accessory;
    private string current = "";

    public OuroTabBar()
    {
        DefaultStyleKey = typeof(OuroTabBar);
        IsTabStop = false;
    }

    /// <summary>Names the navigation for a screen reader, such as "Main". It is not shown.</summary>
    public string Label { get; set; } = "";

    /// <summary>The tabs, in order: two to five.</summary>
    public IReadOnlyList<OuroTabBarItem> Items
    {
        get => items;
        set { items = value; Build(); }
    }

    /// <summary>One optional item that sits apart in a circle at the end. Only its icon shows; its label names it.</summary>
    public OuroTabBarItem? Accessory
    {
        get => accessory;
        set { accessory = value; Build(); }
    }

    /// <summary>The label of the current page's tab.</summary>
    public string Current
    {
        get => current;
        set { current = value; Apply(); }
    }

    /// <summary>A tab or the accessory was pressed, with its label. The app navigates; set <see cref="Current"/> to match.</summary>
    public event EventHandler<string>? Pressed;

    /// <summary>The items now, which the tests and the report read.</summary>
    internal IReadOnlyList<OuroListButton> Buttons => buttons;

    internal OuroListButton? CurrentButton => buttons.FirstOrDefault(b => b.IsCurrent);

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        root = GetTemplateChild("Root") as Grid;
        Build();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroTabBarAutomationPeer(this);

    private void Build()
    {
        buttons.Clear();
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();
        foreach (var item in items) Add(item, false);
        if (accessory is not null) Add(accessory, true);
        if (root is null) return;
        root.Children.Clear();
        root.Children.Add(grid);
        Apply();
    }

    private void Add(OuroTabBarItem item, bool apart)
    {
        var button = new OuroListButton { Label = item.Label, Glyph = item.Glyph, IsAccessory = apart, Container = this };
        void Press() => Pressed?.Invoke(this, item.Label);
        button.Choose = Press;
        button.Click += (_, _) => Press();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = apart ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(button, grid.ColumnDefinitions.Count - 1);
        buttons.Add(button);
        grid.Children.Add(button);
    }

    private void Apply()
    {
        foreach (var b in buttons) b.IsCurrent = !b.IsAccessory && b.Label == current;
    }
}

/// <summary>A tab bar to UI Automation: a List named by its label, whose Selection pattern says which tab is current.</summary>
public sealed partial class OuroTabBarAutomationPeer(OuroTabBar owner) : FrameworkElementAutomationPeer(owner), ISelectionProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;

    protected override string GetClassNameCore() => nameof(OuroTabBar);

    protected override string GetNameCore() => owner.Label;

    protected override bool IsKeyboardFocusableCore() => false;

    protected override object GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Selection ? this : base.GetPatternCore(patternInterface);

    public bool CanSelectMultiple => false;

    public bool IsSelectionRequired => false;

    public IRawElementProviderSimple[] GetSelection() =>
        owner.CurrentButton is { } b ? [ProviderFromPeer(CreatePeerForElement(b))] : [];
}
