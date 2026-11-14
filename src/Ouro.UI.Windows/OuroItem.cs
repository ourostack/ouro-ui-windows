// Core's Item (vendor/core/components/item.spec.json): a row with a title, a description, leading media and trailing
// actions. A row someone can press (it has a Click handler) is one UI Automation Button named by its title, with its
// description as help text, the Invoke pattern, keyboard focus and a 44 epx height; otherwise it is a Group.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.ViewManagement;

namespace Ouro.UI.Windows;

/// <summary>The leading media: a muted icon tile (hidden from assistive technology), or a 40, 64 or 112 epx picture.</summary>
public enum OuroMediaSize
{
    /// <summary>A 40 epx muted tile with a symbol, decorative.</summary>
    Icon,
    /// <summary>A 40 epx picture.</summary>
    Small,
    /// <summary>A 64 epx picture.</summary>
    Medium,
    /// <summary>A 112 epx picture.</summary>
    Large,
}

/// <summary>A row in a list; pressable when it opens something.</summary>
public sealed partial class OuroItem : Control
{
    /// <summary>Core's metrics, in effective pixels.</summary>
    public const double PressableMinHeight = 44, Gap = 16;

    private TextBlock? title, description;
    private FrameworkElement? media, actions;
    private Border? tile;
    private RoutedEventHandler? click;
    private bool over, down;

    public static readonly DependencyProperty TitleProperty = Register(nameof(Title), typeof(string), "");
    public static readonly DependencyProperty DescriptionProperty = Register(nameof(Description), typeof(string), "");
    public static readonly DependencyProperty MediaProperty = Register(nameof(Media), typeof(object), null);
    public static readonly DependencyProperty ActionsProperty = Register(nameof(Actions), typeof(object), null);
    public static readonly DependencyProperty VariantProperty = Register(nameof(Variant), typeof(OuroFrame), OuroFrame.Plain);
    public static readonly DependencyProperty DensityProperty = Register(nameof(Density), typeof(OuroDensity), OuroDensity.Medium);
    public static readonly DependencyProperty MediaSizeProperty = Register(nameof(MediaSize), typeof(OuroMediaSize), OuroMediaSize.Small);

    public OuroItem()
    {
        DefaultStyleKey = typeof(OuroItem);
        IsTabStop = false;
        UseSystemFocusVisuals = true;
        OuroUI.Follow(this, Refresh);
        SizeChanged += (_, _) => Refresh();
    }

    /// <summary>What the row is, such as a recipe's name: its spoken name.</summary>
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>A short line under the title; the help text when pressable. Hidden when empty.</summary>
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    /// <summary>An icon or a picture before the words.</summary>
    public object? Media { get => GetValue(MediaProperty); set => SetValue(MediaProperty, value); }

    /// <summary>Controls after the words, such as a badge or a button. They go under the words at large text sizes.</summary>
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }

    /// <summary>Core's variant: plain (rows in a group) or outline (a row on its own).</summary>
    public OuroFrame Variant { get => (OuroFrame)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }

    /// <summary>Core's size: medium (12 above and below) or small (8).</summary>
    public OuroDensity Density { get => (OuroDensity)GetValue(DensityProperty); set => SetValue(DensityProperty, value); }

    /// <summary>Core's mediaSize.</summary>
    public OuroMediaSize MediaSize { get => (OuroMediaSize)GetValue(MediaSizeProperty); set => SetValue(MediaSizeProperty, value); }

    /// <summary>Whether a person can press it: true once it has a Click handler.</summary>
    public bool IsPressable => click is not null;

    /// <summary>The row was pressed: a click, a tap, Enter or Space while focused, or an accessibility Invoke.</summary>
    public event RoutedEventHandler Click
    {
        add { click += value; Refresh(); }
        remove { click -= value; Refresh(); }
    }

    private static DependencyProperty Register(string name, Type type, object? fallback) =>
        DependencyProperty.Register(name, type, typeof(OuroItem), new PropertyMetadata(fallback, (d, _) => ((OuroItem)d).Refresh()));

    /// <summary>Presses it, as a person would.</summary>
    public void Press()
    {
        if (IsPressable) click?.Invoke(this, new RoutedEventArgs());
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        title = GetTemplateChild("TitleText") as TextBlock;
        description = GetTemplateChild("DescriptionText") as TextBlock;
        media = GetTemplateChild("MediaPresenter") as FrameworkElement;
        actions = GetTemplateChild("ActionsPresenter") as FrameworkElement;
        tile = GetTemplateChild("MediaTile") as Border;
        Refresh();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroItemAutomationPeer(this);

    protected override void OnPointerEntered(PointerRoutedEventArgs e) { base.OnPointerEntered(e); over = true; Refresh(); }

    protected override void OnPointerExited(PointerRoutedEventArgs e) { base.OnPointerExited(e); over = down = false; Refresh(); }

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsPressable) return;
        down = true;
        CapturePointer(e.Pointer);
        Refresh();
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!down) return;
        down = false;
        ReleasePointerCapture(e.Pointer);
        Refresh();
        if (over) Press();
    }

    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e) { base.OnPointerCaptureLost(e); down = false; Refresh(); }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (IsPressable && e.Key is VirtualKey.Enter or VirtualKey.Space)
        {
            e.Handled = true;
            Press();
            return;
        }
        base.OnKeyDown(e);
    }

    private void Refresh()
    {
        var c = OuroUI.Colors(this);
        var hc = OuroUI.HighContrast;
        uint surface = hc ? OuroUI.System(UIElementType.Window) : OuroUI.Argb(c.Background);
        uint ink = hc ? OuroUI.System(UIElementType.WindowText) : OuroUI.Argb(c.Foreground);
        uint muted = hc ? ink : OuroUI.Argb(c.MutedForeground);
        uint hover = hc ? surface : OuroUI.Argb(c.Accent);
        var fill = IsPressable && down ? OuroPaint.Mix(hover, ink, OuroPaint.HoverStep) : IsPressable && over ? hover : 0;
        Background = OuroUI.Brush(fill == 0 ? 0x00FFFFFF & surface : fill);
        Foreground = OuroUI.Brush(ink);
        BorderBrush = OuroUI.Brush(hc ? ink : OuroUI.Argb(c.Border));
        BorderThickness = new Thickness(Variant == OuroFrame.Outline || hc && IsPressable && FocusState != FocusState.Unfocused ? 1 : 0);
        CornerRadius = new CornerRadius(OuroUI.Theme.Radius / 2);
        var y = Density == OuroDensity.Small ? 8 : 12;
        var x = Variant == OuroFrame.Outline ? 16 : 0;
        Padding = new Thickness(x, y, x, y);
        MinHeight = IsPressable ? PressableMinHeight : 0;
        IsTabStop = IsPressable;
        if (description is not null)
        {
            description.Foreground = OuroUI.Brush(muted);
            description.Visibility = string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible;
        }
        if (title is not null) title.Foreground = Foreground;
        if (tile is not null)
        {
            var side = MediaSize switch { OuroMediaSize.Medium => 64, OuroMediaSize.Large => 112, _ => 40 };
            tile.Width = tile.Height = side;
            tile.Background = OuroUI.Brush(MediaSize == OuroMediaSize.Icon ? (hc ? surface : OuroUI.Argb(c.Muted)) : 0);
            tile.Visibility = Media is null ? Visibility.Collapsed : Visibility.Visible;
            AutomationProperties.SetAccessibilityView(tile, MediaSize == OuroMediaSize.Icon ? AccessibilityView.Raw : AccessibilityView.Content);
            if (media is ContentPresenter presenter && MediaSize == OuroMediaSize.Icon) presenter.Foreground = OuroUI.Brush(muted);
        }
        var stacked = OuroUI.TextScale >= OuroUI.StackAtTextScale;
        if (actions is not null)
        {
            Grid.SetRow(actions, stacked ? 1 : 0);
            Grid.SetColumn(actions, stacked ? 1 : 2);
            actions.HorizontalAlignment = HorizontalAlignment.Left;
            actions.Margin = new Thickness(stacked ? 0 : Gap, stacked ? 8 : 0, 0, 0);
            actions.Visibility = Actions is null ? Visibility.Collapsed : Visibility.Visible;
        }
        AutomationProperties.SetHelpText(this, Description);
        OuroUI.Report(this, "", "item", ("pressable", IsPressable), ("title", OuroPaint.Hex(ink)), ("description", OuroPaint.Hex(muted)),
            ("surface", OuroPaint.Hex(fill == 0 ? surface : fill)), ("stacked", stacked),
            ("trimmed", (title?.IsTextTrimmed ?? false) || (description?.IsTextTrimmed ?? false)), ("textHeight", Math.Round(title?.ActualHeight ?? 0)));
    }
}

/// <summary>An item to UI Automation: a Button with Invoke when pressable, else a Group; named by its title.</summary>
public sealed partial class OuroItemAutomationPeer(OuroItem owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => owner.IsPressable ? AutomationControlType.Button : AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(OuroItem);

    protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : owner.Title;

    protected override bool IsKeyboardFocusableCore() => owner.IsPressable;

    protected override object GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Invoke && owner.IsPressable ? this : base.GetPatternCore(patternInterface);

    /// <summary>Presses the row.</summary>
    public void Invoke() => owner.Press();
}
