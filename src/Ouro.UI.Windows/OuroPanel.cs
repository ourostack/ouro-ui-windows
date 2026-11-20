// The panel Core's Dialog, Sheet and ConfirmDialog share (vendor/core/components/{dialog,sheet,confirm-dialog}.spec.json): a
// surface with a title, a line under it, content and a footer, drawn in the window itself rather than in a ContentDialog,
// so it needs no XamlRoot and UI Automation reads it the same way every time. OuroOverlay puts one over a page.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.UI.ViewManagement;
using VirtualKey = global::Windows.System.VirtualKey;

namespace Ouro.UI.Windows;

/// <summary>What a Dialog, a Sheet and a ConfirmDialog have in common. Use one of them, not this.</summary>
public abstract partial class OuroPanel : UserControl
{
    private readonly Border frame = new() { BorderThickness = new Thickness(1) };
    private readonly StackPanel stack = new();
    private readonly Grid header = new();
    private readonly StackPanel heading = new() { Spacing = 4 };
    private readonly TextBlock titleText = new() { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock descriptionText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ContentPresenter body = new();
    private readonly StackPanel footerRow = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly ContentPresenter footerContent = new();
    private OuroButton? close;

    public static readonly DependencyProperty TitleProperty = Register(nameof(Title), typeof(string), "");
    public static readonly DependencyProperty DescriptionProperty = Register(nameof(Description), typeof(string), "");
    public static readonly DependencyProperty HideTitleProperty = Register(nameof(HideTitle), typeof(bool), false);
    public static readonly DependencyProperty CloseLabelProperty = Register(nameof(CloseLabel), typeof(string), "Close");
    public static readonly DependencyProperty PanelContentProperty = Register(nameof(PanelContent), typeof(object), null);
    public static readonly DependencyProperty FooterProperty = Register(nameof(Footer), typeof(object), null);

    protected OuroPanel()
    {
        IsTabStop = true;
        AutomationProperties.SetHeadingLevel(titleText, AutomationHeadingLevel.Level2);
        heading.Children.Add(titleText);
        heading.Children.Add(descriptionText);
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnSpacing = 8;
        header.Children.Add(heading);
        footerRow.Children.Add(footerContent);
        stack.Children.Add(header);
        stack.Children.Add(body);
        stack.Children.Add(footerRow);
        frame.Child = stack;
        base.Content = frame;
        KeyDown += OnKeyDown;
        OuroUI.Follow(this, Refresh);
        SizeChanged += (_, _) => Refresh();
        TabFocusNavigation = KeyboardNavigationMode.Cycle;
        Loaded += (_, _) => { if (TakesFocus) DispatcherQueue.TryEnqueue(() => InitialFocus().Focus(FocusState.Programmatic)); };
    }

    /// <summary>What the panel is about: its spoken name and, unless <see cref="HideTitle"/>, a level 2 heading.</summary>
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>A line under the title, in muted colour. Hidden when empty.</summary>
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    /// <summary>Keeps the title for screen readers and leaves it off the screen.</summary>
    public bool HideTitle { get => (bool)GetValue(HideTitleProperty); set => SetValue(HideTitleProperty, value); }

    /// <summary>The close button's spoken name.</summary>
    public string CloseLabel { get => (string)GetValue(CloseLabelProperty); set => SetValue(CloseLabelProperty, value); }

    /// <summary>The panel's content slot.</summary>
    public object? PanelContent { get => GetValue(PanelContentProperty); set => SetValue(PanelContentProperty, value); }

    /// <summary>The panel's footer slot, such as a Save button.</summary>
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }

    /// <summary>Whether the panel is open. <see cref="Close"/> sets it false and hides the panel.</summary>
    public bool IsOpen { get; private set; } = true;

    /// <summary>Whether a press on the scrim around the panel closes it.</summary>
    public abstract bool ClosesOnOutsidePress { get; }

    /// <summary>The panel closed, and why.</summary>
    public event EventHandler<OuroCloseReason>? Closed;

    /// <summary>Closes the panel for the reason, unless the reason can't close this one (an outside press on a panel that ignores it).</summary>
    public bool Close(OuroCloseReason reason)
    {
        if (!IsOpen || !OuroPanelRules.MayClose(reason, ClosesOnOutsidePress)) return false;
        IsOpen = false;
        Visibility = Visibility.Collapsed;
        Closed?.Invoke(this, reason);
        return true;
    }

    /// <summary>The control that takes focus when the panel opens: the panel itself, or a confirm dialog's Cancel.</summary>
    protected virtual Control InitialFocus() => this;

    /// <summary>Whether the panel takes focus when it opens: not a non-modal sheet, which leaves focus on the page.</summary>
    protected virtual bool TakesFocus => true;

    /// <summary>The kind in the self-report, and the facts the claims read beyond the shared ones.</summary>
    protected abstract string Kind { get; }

    /// <summary>The words under the title.</summary>
    protected virtual string DescriptionShown => Description;

    /// <summary>Whether the panel has a close button: a confirm dialog's Cancel does that job.</summary>
    protected virtual bool HasClose => true;

    /// <summary>Where the footer's own buttons go, before the footer slot.</summary>
    protected Panel FooterButtons => footerRow;

    /// <summary>Extra self-report facts the subclass adds.</summary>
    internal virtual (string, object?)[] Facts() => [];

    /// <summary>Places the panel in the host: its alignment and size on screen.</summary>
    internal abstract void Place(FrameworkElement scrim, double hostWidth, double hostHeight);

    /// <summary>The frame's corner radius for the sides that touch an edge of the window.</summary>
    protected CornerRadius Corners { get; set; } = new(OuroUI.Theme.Radius);

    /// <summary>The frame, so a subclass can set its width.</summary>
    protected Border Frame => frame;

    /// <summary>Whether the footer stacks its buttons: at large text sizes, so each keeps its words.</summary>
    protected static bool Stacked => OuroUI.TextScale >= OuroUI.StackAtTextScale;

    private static DependencyProperty Register(string name, Type type, object? fallback) =>
        DependencyProperty.Register(name, type, typeof(OuroPanel), new PropertyMetadata(fallback, (d, _) => ((OuroPanel)d).Refresh()));

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        if (Close(OuroCloseReason.Escape)) e.Handled = true;
    }

    /// <summary>Redraws the panel from its properties, the theme and the person's settings.</summary>
    protected void Refresh()
    {
        var c = OuroUI.Colors(this);
        var hc = OuroUI.HighContrast;
        uint surface = hc ? OuroUI.System(UIElementType.Window) : OuroUI.Argb(c.Background);
        uint ink = hc ? OuroUI.System(UIElementType.WindowText) : OuroUI.Argb(c.Foreground);
        uint muted = hc ? ink : OuroUI.Argb(c.MutedForeground);
        uint edge = hc ? ink : OuroUI.Argb(c.Border);
        frame.Background = OuroUI.Brush(surface);
        frame.BorderBrush = OuroUI.Brush(edge);
        frame.CornerRadius = Corners;
        frame.Padding = new Thickness(24);
        stack.Spacing = 16;
        titleText.Text = Title;
        titleText.Foreground = OuroUI.Brush(ink);
        titleText.FontSize = 18;
        titleText.Visibility = string.IsNullOrEmpty(Title) ? Visibility.Collapsed : Visibility.Visible;
        // Hidden visually only: a collapsed title would leave the heading out of the UI Automation tree.
        titleText.Opacity = HideTitle ? 0 : 1;
        titleText.MaxHeight = HideTitle ? 1 : double.PositiveInfinity;
        var shown = DescriptionShown;
        descriptionText.Text = shown;
        descriptionText.Foreground = OuroUI.Brush(muted);
        descriptionText.Visibility = string.IsNullOrEmpty(shown) ? Visibility.Collapsed : Visibility.Visible;
        heading.Visibility = titleText.Visibility == Visibility.Collapsed && descriptionText.Visibility == Visibility.Collapsed ? Visibility.Collapsed : Visibility.Visible;
        if (HasClose)
        {
            if (close is null)
            {
                close = new OuroButton { Tone = OuroTone.Secondary, Variant = OuroVariant.Plain };
                close.Click += (_, _) => Close(OuroCloseReason.Button);
                Grid.SetColumn(close, 1);
                header.Children.Add(close);
            }
            close.Label = CloseLabel;
        }
        // An empty slot is collapsed, not left as an empty presenter (WinUI failed to measure an empty footer, CI 2026-11-14).
        body.Content = PanelContent;
        body.Visibility = PanelContent is null ? Visibility.Collapsed : Visibility.Visible;
        footerContent.Content = Footer;
        footerContent.Visibility = Footer is null ? Visibility.Collapsed : Visibility.Visible;
        footerRow.Orientation = Stacked ? Orientation.Vertical : Orientation.Horizontal;
        footerRow.HorizontalAlignment = Stacked ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        footerRow.Visibility = footerRow.Children.Count > 1 || Footer is not null ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(this, Title);
        var facts = new List<(string, object?)>
        {
            ("title", OuroPaint.Hex(ink)), ("description", OuroPaint.Hex(muted)), ("surface", OuroPaint.Hex(surface)), ("fill", OuroPaint.Hex(surface)), ("edge", OuroPaint.Hex(edge)),
            ("hideTitle", HideTitle), ("close", HasClose ? CloseLabel : "none"), ("closeTarget", Math.Round(close?.ActualHeight ?? 0)),
            ("radius", Math.Round(OuroUI.Theme.Radius)), ("pressScale", OuroUI.PressScale),
            ("stacked", Stacked), ("trimmed", titleText.IsTextTrimmed || descriptionText.IsTextTrimmed), ("textHeight", Math.Round(titleText.ActualHeight)),
        };
        facts.AddRange(Facts());
        OuroUI.Report(this, "", Kind, [.. facts]);
    }

    /// <summary>The panel to UI Automation: named by its title.</summary>
    protected abstract AutomationControlType Role { get; }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroPanelAutomationPeer(this);

    internal AutomationControlType PeerType => Role;
    internal string PeerName => Title;
}

/// <summary>A panel to UI Automation: a Window (Dialog, ConfirmDialog) or a Pane (Sheet) named by its title, focusable.</summary>
public sealed partial class OuroPanelAutomationPeer(OuroPanel owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => owner.PeerType;

    protected override string GetClassNameCore() => owner.GetType().Name;

    protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : owner.PeerName;

    protected override bool IsKeyboardFocusableCore() => true;
}
