// Core's Switch (vendor/core/components/switch.spec.json) as a templated WinUI ToggleButton, so it keeps WinUI's Space
// key, focus ring and Toggle pattern. UI Automation sees one Button with the Toggle pattern (On, Off), as it sees a
// WinUI ToggleSwitch, named by its label (", required" when it is) with the description as help text. The row is at
// least 44 epx tall; the thumb stretches while pressed, never with Windows animations off.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Ouro.UI.Hx.Tokens;

namespace Ouro.UI.Windows;

/// <summary>A switch: on or off, taking effect at once. For a choice made with a button press afterwards, use <see cref="OuroCheckbox"/>.</summary>
public sealed partial class OuroSwitch : ToggleButton
{
    /// <summary>Core's metrics, in effective pixels: the track, the row's minimum height, the gap to the words and the thumb.</summary>
    public const double TrackWidth = 40, TrackHeight = 24, MinRowHeight = 44, Gap = 12, Thumb = 16, ThumbInset = 4, ThumbStretch = 4;

    private Border? track, thumb;
    private TextBlock? label, description;

    public static readonly DependencyProperty LabelProperty = Register(nameof(Label), typeof(string), "");
    public static readonly DependencyProperty DescriptionProperty = Register(nameof(Description), typeof(string), "");
    public static readonly DependencyProperty IsRequiredProperty = Register(nameof(IsRequired), typeof(bool), false);
    public static readonly DependencyProperty HideLabelProperty = Register(nameof(HideLabel), typeof(bool), false);

    public OuroSwitch()
    {
        DefaultStyleKey = typeof(OuroSwitch);
        OuroUI.Follow(this, Refresh);
        RegisterPropertyChangedCallback(IsPressedProperty, (_, _) => Refresh());
        RegisterPropertyChangedCallback(IsPointerOverProperty, (_, _) => Refresh());
        RegisterPropertyChangedCallback(IsEnabledProperty, (_, _) => Refresh());
        RegisterPropertyChangedCallback(IsCheckedProperty, (_, _) => Refresh());
        SizeChanged += (_, _) => Refresh();
    }

    /// <summary>What the switch controls, beside the track, and its spoken name.</summary>
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <summary>Supporting words under the label, read after the name. Hidden when empty.</summary>
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    /// <summary>The switch must be on: the label gets a marker and the name says required.</summary>
    public bool IsRequired { get => (bool)GetValue(IsRequiredProperty); set => SetValue(IsRequiredProperty, value); }

    /// <summary>Hides the label's words; the switch keeps the label as its name.</summary>
    public bool HideLabel { get => (bool)GetValue(HideLabelProperty); set => SetValue(HideLabelProperty, value); }

    /// <summary>Whether it is on. A switch has no mixed state, so this is a plain bool.</summary>
    public bool IsOn { get => IsChecked == true; set => IsChecked = value; }

    private static DependencyProperty Register(string name, System.Type type, object fallback) =>
        DependencyProperty.Register(name, type, typeof(OuroSwitch), new PropertyMetadata(fallback, (d, _) => ((OuroSwitch)d).Refresh()));

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        track = GetTemplateChild("Track") as Border;
        thumb = GetTemplateChild("Thumb") as Border;
        label = GetTemplateChild("LabelText") as TextBlock;
        description = GetTemplateChild("DescriptionText") as TextBlock;
        Refresh();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroSwitchAutomationPeer(this);

    /// <summary>The spoken name: the label, with ", required" when it is.</summary>
    internal string SpokenName => OuroFormRules.Name(Label, IsRequired);

    private void Refresh()
    {
        var p = OuroUI.SurfacePaint(this);
        var on = IsChecked == true;
        MinHeight = MinRowHeight;
        MinWidth = MinRowHeight;
        Opacity = IsEnabled ? 1 : 0.5;
        var stretched = IsPressed && OuroUI.AnimationsEnabled;
        var thumbWidth = Thumb + (stretched ? ThumbStretch : 0);
        if (track is not null)
        {
            track.Width = TrackWidth;
            track.Height = TrackHeight;
            track.CornerRadius = new CornerRadius(TrackHeight / 2);
            track.Background = OuroUI.Brush(on ? p.Checked : p.Field);
            track.BorderBrush = OuroUI.Brush(on ? p.Checked : p.Edge);
            track.BorderThickness = new Thickness(2);
        }
        if (thumb is not null)
        {
            thumb.Width = thumbWidth;
            thumb.Height = Thumb;
            thumb.CornerRadius = new CornerRadius(Thumb / 2);
            // On, the thumb keeps its right edge where it was; off, its left edge.
            thumb.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            thumb.Margin = new Thickness(ThumbInset - 2);
            thumb.Background = OuroUI.Brush(on ? p.Tick : p.Edge);
        }
        if (label is not null)
        {
            label.Inlines.Clear();
            label.Inlines.Add(new Run { Text = Label });
            if (IsRequired) label.Inlines.Add(new Run { Text = " *", Foreground = OuroUI.Brush(p.Error) });
            label.Foreground = OuroUI.Brush(p.Text);
            label.Visibility = HideLabel || string.IsNullOrEmpty(Label) ? Visibility.Collapsed : Visibility.Visible;
        }
        if (description is not null)
        {
            description.Text = Description;
            description.Foreground = OuroUI.Brush(p.Muted);
            description.Visibility = string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible;
        }
        AutomationProperties.SetHelpText(this, Description);
        AutomationProperties.SetIsRequiredForForm(this, IsRequired);
        // The thumb's stretch follows the same animation setting as core's press scale, so the motion check reads that.
        OuroUI.Report(this, "", "switch", ("label", OuroPaint.Hex(p.Text)), ("description", OuroPaint.Hex(p.Muted)),
            ("edge", OuroPaint.Hex(on ? p.Checked : p.Edge)), ("checked", OuroPaint.Hex(p.Checked)), ("thumb", OuroPaint.Hex(on ? p.Tick : p.Edge)),
            ("track", OuroPaint.Hex(on ? p.Checked : p.Field)), ("behind", OuroPaint.Hex(p.Behind)), ("state", on ? "on" : "off"),
            ("pressScale", OuroUI.PressScale), ("required", IsRequired), ("described", !string.IsNullOrEmpty(Description)),
            ("trimmed", (label?.IsTextTrimmed ?? false) || (description?.IsTextTrimmed ?? false)), ("textHeight", Math.Round(label?.ActualHeight ?? 0)));
    }
}

/// <summary>A switch to UI Automation: a Button with the Toggle pattern, as WinUI's ToggleSwitch is, named by its label.</summary>
public sealed partial class OuroSwitchAutomationPeer(OuroSwitch owner) : ToggleButtonAutomationPeer(owner)
{
    protected override string GetClassNameCore() => nameof(OuroSwitch);

    protected override string GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : owner.SpokenName;
}
