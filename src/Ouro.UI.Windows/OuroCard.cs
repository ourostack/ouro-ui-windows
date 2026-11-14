// Core's Card (vendor/core/components/card.spec.json): a titled surface with content, an action and a footer. UI
// Automation sees a Group named by its title, whose title is a level 2 heading; its controls keep their own peers.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;

namespace Ouro.UI.Windows;

/// <summary>Outline draws the card's edge; plain draws its surface only.</summary>
public enum OuroFrame
{
    /// <summary>A bordered surface.</summary>
    Outline,
    /// <summary>No frame: for rows in a group, or a card inside a card.</summary>
    Plain,
}

/// <summary>Space inside a card or an item.</summary>
public enum OuroDensity
{
    /// <summary>Medium, the default.</summary>
    Medium,
    /// <summary>Small.</summary>
    Small,
}

/// <summary>A titled surface that groups content, with an optional action beside the title and a footer.</summary>
public sealed partial class OuroCard : ContentControl
{
    private TextBlock? title, description;
    private Grid? header;
    private FrameworkElement? action;
    private StackPanel? stack;

    public static readonly DependencyProperty TitleProperty = Register(nameof(Title), typeof(string), "");
    public static readonly DependencyProperty DescriptionProperty = Register(nameof(Description), typeof(string), "");
    public static readonly DependencyProperty ActionProperty = Register(nameof(Action), typeof(object), null);
    public static readonly DependencyProperty FooterProperty = Register(nameof(Footer), typeof(object), null);
    public static readonly DependencyProperty VariantProperty = Register(nameof(Variant), typeof(OuroFrame), OuroFrame.Outline);
    public static readonly DependencyProperty DensityProperty = Register(nameof(Density), typeof(OuroDensity), OuroDensity.Medium);

    public OuroCard()
    {
        DefaultStyleKey = typeof(OuroCard);
        IsTabStop = false;
        OuroUI.Follow(this, Refresh);
        SizeChanged += (_, _) => Refresh();
    }

    /// <summary>What the card is about: its spoken name and a level 2 heading.</summary>
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>A line under the title, in muted colour. Hidden when empty.</summary>
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    /// <summary>A control beside the title, such as an Edit button. It goes under the title at large text sizes.</summary>
    public object? Action { get => GetValue(ActionProperty); set => SetValue(ActionProperty, value); }

    /// <summary>Content at the bottom, such as a total or a secondary button.</summary>
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }

    /// <summary>Core's variant: outline or plain.</summary>
    public OuroFrame Variant { get => (OuroFrame)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }

    /// <summary>Core's size: medium (24 padding, 16 gap) or small (16, 12).</summary>
    public OuroDensity Density { get => (OuroDensity)GetValue(DensityProperty); set => SetValue(DensityProperty, value); }

    private static DependencyProperty Register(string name, Type type, object? fallback) =>
        DependencyProperty.Register(name, type, typeof(OuroCard), new PropertyMetadata(fallback, (d, _) => ((OuroCard)d).Refresh()));

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        title = GetTemplateChild("TitleText") as TextBlock;
        description = GetTemplateChild("DescriptionText") as TextBlock;
        header = GetTemplateChild("Header") as Grid;
        action = GetTemplateChild("ActionPresenter") as FrameworkElement;
        stack = GetTemplateChild("Stack") as StackPanel;
        Refresh();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroCardAutomationPeer(this);

    private void Refresh()
    {
        var c = OuroUI.Colors(this);
        var hc = OuroUI.HighContrast;
        uint surface = hc ? OuroUI.System(UIElementType.Window) : OuroUI.Argb(c.Card);
        uint ink = hc ? OuroUI.System(UIElementType.WindowText) : OuroUI.Argb(c.CardForeground);
        uint muted = hc ? ink : OuroUI.Argb(c.MutedForeground);
        uint edge = hc ? ink : OuroUI.Argb(c.Border);
        Background = OuroUI.Brush(Variant == OuroFrame.Plain && !hc ? 0 : surface);
        Foreground = OuroUI.Brush(ink);
        BorderBrush = OuroUI.Brush(edge);
        BorderThickness = new Thickness(Variant == OuroFrame.Outline ? 1 : 0);
        CornerRadius = new CornerRadius(OuroUI.Theme.Radius);
        var small = Density == OuroDensity.Small;
        Padding = new Thickness(small ? 16 : 24);
        if (stack is not null) stack.Spacing = small ? 12 : 16;
        if (description is not null)
        {
            description.Foreground = OuroUI.Brush(muted);
            description.Visibility = string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible;
        }
        if (title is not null) title.Foreground = Foreground;
        var stacked = OuroUI.TextScale >= OuroUI.StackAtTextScale;
        if (action is not null && header is not null)
        {
            Grid.SetRow(action, stacked ? 1 : 0);
            Grid.SetColumn(action, stacked ? 0 : 1);
            action.HorizontalAlignment = stacked ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            action.Visibility = Action is null ? Visibility.Collapsed : Visibility.Visible;
        }
        var behind = Variant == OuroFrame.Plain && !hc ? OuroUI.Argb(c.Background) : surface;
        OuroUI.Report(this, "", "card", ("title", OuroPaint.Hex(ink)), ("description", OuroPaint.Hex(muted)), ("surface", OuroPaint.Hex(behind)),
            ("stacked", stacked), ("trimmed", (title?.IsTextTrimmed ?? false) || (description?.IsTextTrimmed ?? false)), ("textHeight", Math.Round(title?.ActualHeight ?? 0)));
    }
}

/// <summary>A card to UI Automation: a Group named by its title.</summary>
public sealed partial class OuroCardAutomationPeer(OuroCard owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(OuroCard);

    protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : owner.Title;
}
