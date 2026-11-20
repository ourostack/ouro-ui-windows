// The pressable part of Core's TabBar (vendor/core/components/tab-bar.spec.json): one item, drawn as a templated WinUI Button
// so it keeps WinUI's Enter and Space keys, focus ring and press tracking. Use OuroTabBar, which builds and wires these.
// UI Automation sees a ListItem named by its label with the SelectionItem pattern, the current one selected, as a
// NavigationView's top-level items are. The item scales to core's press scale while pressed, never with Windows animations off.
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Ouro.UI.Hx.Tokens;

namespace Ouro.UI.Windows;

/// <summary>One item in a tab bar: an icon over its words, on an accent wash while it is the current one.</summary>
public sealed partial class OuroListButton : Button
{
    /// <summary>Core's metrics, in effective pixels: the bar's height, the icon, and the circle of an accessory.</summary>
    public const double BarHeight = 64, IconSize = 24, AccessorySize = 48;

    private readonly TextBlock label = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 12, IsTextScaleFactorEnabled = true, HighContrastAdjustment = ElementHighContrastAdjustment.None };
    private readonly FontIcon icon = new() { FontSize = IconSize, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"), IsTextScaleFactorEnabled = false };
    private bool current, accessory;
    private string text = "";

    public OuroListButton()
    {
        DefaultStyleKey = typeof(OuroListButton);
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        Content = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { icon, label } };
        OuroUI.Follow(this, Refresh);
        RegisterPropertyChangedCallback(IsPressedProperty, (_, _) => Press());
        SizeChanged += (_, _) => Refresh();
        label.SizeChanged += (_, _) => Refresh();
    }

    /// <summary>The words under the icon, which are also its spoken name.</summary>
    public string Label
    {
        get => text;
        set { text = value; label.Text = value; Refresh(); }
    }

    /// <summary>The icon, as a glyph of Segoe Fluent Icons. It is decorative: the label names the item.</summary>
    public string Glyph
    {
        get => icon.Glyph;
        set => icon.Glyph = value;
    }

    /// <summary>Whether this is the current page: accent words on an accent wash.</summary>
    public bool IsCurrent
    {
        get => current;
        set
        {
            if (current == value) return;
            current = value;
            (FrameworkElementAutomationPeer.FromElement(this) as OuroListButtonAutomationPeer)?.Raise(!value, value);
            Refresh();
        }
    }

    /// <summary>Whether it sits apart in a circle at the end of the bar, with only its icon showing.</summary>
    public bool IsAccessory
    {
        get => accessory;
        set { accessory = value; Refresh(); }
    }

    /// <summary>The bar this item belongs to, which names it to UI Automation.</summary>
    internal FrameworkElement? Container { get; set; }

    /// <summary>What UI Automation's Select asks for: the bar reports the press.</summary>
    internal Action? Choose { get; set; }

    /// <summary>The item's words are cut off.</summary>
    internal bool Trimmed => label.IsTextTrimmed;

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroListButtonAutomationPeer(this);

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
        var wash = current ? OuroNav.Wash(p.Behind, p.Checked) : 0u;
        var behind = current ? wash : p.Behind;
        var words = current ? OuroNav.CurrentWords(p.Checked, wash, p.Text) : p.Muted;
        Background = OuroUI.Brush(wash);
        Foreground = OuroUI.Brush(words);
        label.Foreground = Foreground;
        icon.Foreground = Foreground;
        label.Visibility = accessory ? Visibility.Collapsed : Visibility.Visible;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(8, 6, 8, 6);
        CornerRadius = new CornerRadius(accessory ? AccessorySize / 2 : 16);
        MinWidth = accessory ? AccessorySize : 44;
        MinHeight = accessory ? AccessorySize : BarHeight;
        HorizontalAlignment = accessory ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        OuroUI.Report(this, "", "tab-bar",
            ("label", OuroPaint.Hex(words)), ("wash", OuroPaint.Hex(wash)), ("behind", OuroPaint.Hex(behind)), ("current", current), ("accessory", accessory),
            ("list", Container is null ? "" : AutomationProperties.GetAutomationId(Container)), ("pressScale", OuroUI.PressScale), ("trimmed", label.IsTextTrimmed), ("textHeight", Math.Round(label.ActualHeight)));
    }
}

/// <summary>A tab bar's item to UI Automation: a ListItem named by its label, selected while it is the current page.</summary>
public sealed partial class OuroListButtonAutomationPeer(OuroListButton owner) : ButtonAutomationPeer(owner), ISelectionItemProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

    protected override string GetClassNameCore() => nameof(OuroListButton);

    protected override string GetNameCore() => owner.Label;

    protected override string GetAutomationIdCore() => owner.Container is { } c ? $"{AutomationProperties.GetAutomationId(c)}.{owner.Label}" : base.GetAutomationIdCore();

    protected override object GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.SelectionItem ? this : base.GetPatternCore(patternInterface);

    public bool IsSelected => owner.IsCurrent;

    public IRawElementProviderSimple SelectionContainer => ProviderFromPeer(CreatePeerForElement(owner.Container ?? owner));

    public void Select() => owner.Choose?.Invoke();

    public void AddToSelection() => Select();

    public void RemoveFromSelection()
    {
    }

    internal void Raise(bool was, bool now) => RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, was, now);
}
