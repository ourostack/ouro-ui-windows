// The pressable part of Core's Tabs (vendor/core/components/tabs.spec.json): one tab, drawn as a templated WinUI Button so
// it keeps WinUI's Enter and Space keys, focus ring and press tracking. Use OuroTabs, which builds and wires these.
// UI Automation sees a TabItem named by its label with the SelectionItem pattern, and no Invoke. The tab scales to core's
// press scale while pressed, never with Windows animations off.
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Shapes = Microsoft.UI.Xaml.Shapes;
using Ouro.UI.Hx.Tokens;

namespace Ouro.UI.Windows;

/// <summary>One tab in a tab list: its words, with a line under it (beside it when vertical) while it is the selected one.</summary>
public sealed partial class OuroNavButton : Button
{
    /// <summary>Core's metrics, in effective pixels: the touch target and the selection line.</summary>
    public const double MinTarget = 44, LineThickness = 2;

    private readonly TextBlock label = new() { TextWrapping = TextWrapping.Wrap, IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None };
    private Shapes.Rectangle? bar;
    private bool selected, vertical;
    private string text = "";

    public OuroNavButton()
    {
        DefaultStyleKey = typeof(OuroNavButton);
        Content = label;
        OuroUI.Follow(this, Refresh);
        RegisterPropertyChangedCallback(IsPressedProperty, (_, _) => Press());
        RegisterPropertyChangedCallback(IsEnabledProperty, (_, _) => Refresh());
        SizeChanged += (_, _) => Refresh();
        label.SizeChanged += (_, _) => Refresh();
    }

    /// <summary>The words on it, which are also its spoken name.</summary>
    public string Label
    {
        get => text;
        set { text = value; label.Text = value; Refresh(); }
    }

    /// <summary>The tab's value, which the tab list reports when it is pressed or selected.</summary>
    public string Value { get; set; } = "";

    /// <summary>Whether this is the selected tab: in the foreground, with the line, and the list's one tab stop.</summary>
    public bool IsSelected
    {
        get => selected;
        set
        {
            if (selected == value) return;
            selected = value;
            (FrameworkElementAutomationPeer.FromElement(this) as OuroNavButtonAutomationPeer)?.Raise(!value, value);
            Refresh();
        }
    }

    /// <summary>Whether the tab list runs top to bottom, so the line sits beside the words instead of under them.</summary>
    public bool IsVertical
    {
        get => vertical;
        set { vertical = value; Refresh(); }
    }

    /// <summary>The tab list this tab belongs to, which names it to UI Automation.</summary>
    internal FrameworkElement? Container { get; set; }

    /// <summary>What UI Automation's Select asks for: the tab list chooses this tab.</summary>
    internal Action? Choose { get; set; }

    /// <summary>The tab's words are cut off.</summary>
    internal bool Trimmed => label.IsTextTrimmed;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        bar = GetTemplateChild("Bar") as Shapes.Rectangle;
        Refresh();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroNavButtonAutomationPeer(this);

    private void Press()
    {
        CenterPoint = new Vector3((float)ActualWidth / 2, (float)ActualHeight / 2, 0);
        var s = IsPressed ? OuroUI.PressScale : 1f;
        ScaleTransition = OuroUI.AnimationsEnabled ? new Vector3Transition { Duration = TimeSpan.FromMilliseconds(OuroTokens.Duration.Press) } : null;
        Scale = new Vector3(s, s, 1);
    }

    private void Refresh()
    {
        var p = OuroUI.SurfacePaint(this);
        var words = selected ? p.Text : p.Muted;
        Background = OuroUI.Brush(0);
        Foreground = OuroUI.Brush(words);
        label.Foreground = Foreground;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(16, 8, 16, 8);
        MinWidth = MinHeight = MinTarget;
        HorizontalContentAlignment = vertical ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        HorizontalAlignment = vertical ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        Opacity = IsEnabled ? 1 : 0.5;
        if (bar is not null)
        {
            bar.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            bar.Fill = OuroUI.Brush(p.Checked);
            bar.Width = vertical ? LineThickness : double.NaN;
            bar.Height = vertical ? double.NaN : LineThickness;
            bar.HorizontalAlignment = vertical ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            bar.VerticalAlignment = vertical ? VerticalAlignment.Stretch : VerticalAlignment.Bottom;
        }
        OuroUI.Report(this, "", "tabs",
            ("label", OuroPaint.Hex(words)), ("line", OuroPaint.Hex(p.Checked)), ("behind", OuroPaint.Hex(p.Behind)), ("selected", selected), ("vertical", vertical),
            ("list", Container is null ? "" : AutomationProperties.GetAutomationId(Container)), ("pressScale", OuroUI.PressScale), ("trimmed", label.IsTextTrimmed), ("textHeight", Math.Round(label.ActualHeight)));
    }
}

/// <summary>A tab to UI Automation: a TabItem named by its label, selected or not, that can be selected but not invoked.</summary>
public sealed partial class OuroNavButtonAutomationPeer(OuroNavButton owner) : ButtonAutomationPeer(owner), ISelectionItemProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TabItem;

    protected override string GetClassNameCore() => nameof(OuroNavButton);

    protected override string GetNameCore() => owner.Label;

    protected override string GetAutomationIdCore() => owner.Container is { } c ? $"{AutomationProperties.GetAutomationId(c)}.{owner.Value}" : base.GetAutomationIdCore();

    protected override object GetPatternCore(PatternInterface patternInterface) => patternInterface switch
    {
        PatternInterface.SelectionItem => this,
        PatternInterface.Invoke => null!,
        _ => base.GetPatternCore(patternInterface),
    };

    public bool IsSelected => owner.IsSelected;

    public IRawElementProviderSimple SelectionContainer => ProviderFromPeer(CreatePeerForElement(owner.Container ?? owner));

    public void Select() => owner.Choose?.Invoke();

    public void AddToSelection() => Select();

    public void RemoveFromSelection()
    {
    }

    internal void Raise(bool was, bool now) => RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, was, now);
}
