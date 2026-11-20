// Core's Tabs (vendor/core/components/tabs.spec.json): a row of tabs, one of them selected, that switches the panel beneath
// it (beside it when vertical). The list is one tab stop, on the selected tab; the arrow keys that follow the orientation
// move to the next enabled tab and select it, and Home and End go to the first and last. UI Automation sees a Tab named by
// its label, with the Selection pattern, of TabItems; the panel is a Pane named by the selected tab.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Ouro.UI.Windows;

/// <summary>One tab to offer: its value, the words on it, and whether it can be chosen.</summary>
public sealed record OuroTabItem(string Value, string Label, bool Disabled = false);

/// <summary>The panel a tab list switches: put the selected tab's content in it.</summary>
public sealed partial class OuroTabPanel : Grid
{
    /// <summary>The selected tab's words, which name the panel.</summary>
    public string Label { get; set; } = "";

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroTabPanelAutomationPeer(this);
}

/// <summary>A tab panel to UI Automation: a Pane named by its tab.</summary>
public sealed partial class OuroTabPanelAutomationPeer(OuroTabPanel owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;

    protected override string GetClassNameCore() => nameof(OuroTabPanel);

    protected override string GetNameCore() => owner.Label;
}

/// <summary>A list of tabs and the panel it switches.</summary>
public sealed partial class OuroTabs : Control
{
    private readonly StackPanel list = new();
    private readonly OuroTabPanel panel = new();
    private readonly List<OuroNavButton> tabs = [];
    private StackPanel? root;
    private IReadOnlyList<OuroTabItem> items = [];
    private string value = "";
    private bool vertical;

    public OuroTabs()
    {
        DefaultStyleKey = typeof(OuroTabs);
        IsTabStop = false;
        list.KeyDown += OnListKeyDown;
        list.XYFocusKeyboardNavigation = Microsoft.UI.Xaml.Input.XYFocusKeyboardNavigationMode.Disabled;
    }

    /// <summary>Names the tab list for a screen reader, such as "Recipe sections".</summary>
    public string Label { get; set; } = "";

    /// <summary>The tabs, in order.</summary>
    public IReadOnlyList<OuroTabItem> Items
    {
        get => items;
        set { items = value; Build(); }
    }

    /// <summary>The selected tab's value.</summary>
    public string Value
    {
        get => value;
        set { this.value = value; Apply(); }
    }

    /// <summary>Whether the tabs run top to bottom, with the panel beside them.</summary>
    public bool Vertical
    {
        get => vertical;
        set { vertical = value; Build(); }
    }

    /// <summary>The selected tab's content, shown in the panel.</summary>
    public UIElement? Content
    {
        get => panel.Children.FirstOrDefault();
        set
        {
            panel.Children.Clear();
            if (value is not null) panel.Children.Add(value);
        }
    }

    /// <summary>A tab was pressed, with its value, whether or not it was already selected.</summary>
    public event EventHandler<string>? Pressed;

    /// <summary>The selected tab changed, with its value.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>The tabs now, which the tests and the report read.</summary>
    internal IReadOnlyList<OuroNavButton> Tabs => tabs;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        root = GetTemplateChild("Root") as StackPanel;
        Build();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroTabsAutomationPeer(this);

    internal OuroNavButton? SelectedTab => tabs.FirstOrDefault(t => t.IsSelected);

    internal void Choose(OuroNavButton tab, bool focus)
    {
        if (!tab.IsEnabled) return;
        var changed = value != tab.Value;
        value = tab.Value;
        Apply();
        if (focus) tab.Focus(FocusState.Keyboard);
        if (changed) Changed?.Invoke(this, tab.Value);
    }

    private void Build()
    {
        tabs.Clear();
        list.Children.Clear();
        list.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        foreach (var item in items)
        {
            var tab = new OuroNavButton { Label = item.Label, Value = item.Value, IsEnabled = !item.Disabled, IsVertical = vertical, Container = this };
            tab.Choose = () => Choose(tab, false);
            tab.Click += (_, _) =>
            {
                Pressed?.Invoke(this, tab.Value);
                Choose(tab, false);
            };
            tabs.Add(tab);
            list.Children.Add(tab);
        }
        if (root is null) return;
        root.Children.Clear();
        root.Orientation = vertical ? Orientation.Horizontal : Orientation.Vertical;
        root.Children.Add(list);
        root.Children.Add(panel);
        Apply();
    }

    private void Apply()
    {
        var current = tabs.FirstOrDefault(t => t.Value == value && t.IsEnabled) ?? tabs.FirstOrDefault(t => t.IsEnabled);
        foreach (var tab in tabs)
        {
            tab.IsSelected = tab == current;
            tab.IsTabStop = tab == current;
        }
        panel.Label = current?.Label ?? "";
    }

    private void OnListKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var flip = !vertical && FlowDirection == FlowDirection.RightToLeft;
        OuroNavKey? key = e.Key switch
        {
            VirtualKey.Left when !vertical => flip ? OuroNavKey.Next : OuroNavKey.Previous,
            VirtualKey.Right when !vertical => flip ? OuroNavKey.Previous : OuroNavKey.Next,
            VirtualKey.Up when vertical => OuroNavKey.Previous,
            VirtualKey.Down when vertical => OuroNavKey.Next,
            VirtualKey.Home => OuroNavKey.First,
            VirtualKey.End => OuroNavKey.Last,
            _ => null,
        };
        if (key is not { } k) return;
        var from = tabs.FindIndex(t => t.FocusState != FocusState.Unfocused);
        var to = OuroNav.Move(tabs.Select(t => t.IsEnabled).ToList(), from < 0 ? tabs.FindIndex(t => t.IsSelected) : from, k);
        e.Handled = true;
        if (to >= 0) Choose(tabs[to], true);
    }
}

/// <summary>A tab list to UI Automation: a Tab named by its label, whose Selection pattern says which tab is selected.</summary>
public sealed partial class OuroTabsAutomationPeer(OuroTabs owner) : FrameworkElementAutomationPeer(owner), ISelectionProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Tab;

    protected override string GetClassNameCore() => nameof(OuroTabs);

    protected override string GetNameCore() => owner.Label;

    protected override bool IsKeyboardFocusableCore() => false;

    protected override object GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Selection ? this : base.GetPatternCore(patternInterface);

    public bool CanSelectMultiple => false;

    public bool IsSelectionRequired => true;

    public IRawElementProviderSimple[] GetSelection() =>
        owner.SelectedTab is { } t ? [ProviderFromPeer(CreatePeerForElement(t))] : [];
}
