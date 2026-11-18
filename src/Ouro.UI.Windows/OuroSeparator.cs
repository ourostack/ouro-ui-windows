// Core's Separator (vendor/core/components/separator.spec.json): a hairline between groups of content, horizontal or
// vertical, with a short word in its middle or not. UI Automation sees a Separator, or with a label the label as Text
// in its place; the lines are never controls.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Shapes = Microsoft.UI.Xaml.Shapes;

namespace Ouro.UI.Windows;

/// <summary>A thin line between groups of content, with a word such as "or" in its middle or not.</summary>
public sealed partial class OuroSeparator : Control
{
    /// <summary>Core's metrics, in effective pixels: the line's width and the gap around a label.</summary>
    public const double LineWidth = 1, LabelGap = 12;

    private Grid? root;
    private Shapes.Rectangle? start, end;
    private TextBlock? words;

    public static readonly DependencyProperty OrientationProperty = Register(nameof(Orientation), typeof(Orientation), Orientation.Horizontal);
    public static readonly DependencyProperty ToneProperty = Register(nameof(Tone), typeof(OuroTone), OuroTone.Secondary);
    public static readonly DependencyProperty LabelProperty = Register(nameof(Label), typeof(string), "");

    public OuroSeparator()
    {
        DefaultStyleKey = typeof(OuroSeparator);
        IsTabStop = false;
        OuroUI.Follow(this, Refresh);
        SizeChanged += (_, _) => Refresh();
    }

    /// <summary>Horizontal spans its container's width; vertical stands as tall as its row.</summary>
    public Orientation Orientation { get => (Orientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }

    /// <summary>Secondary, the default, draws the border colour; another tone colours the line and the label, for an accent line.</summary>
    public OuroTone Tone { get => (OuroTone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }

    /// <summary>A short word in the middle of a horizontal line, such as "or", read as text in its place.</summary>
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <summary>Whether it shows a label: then UI Automation reads it as text.</summary>
    public bool IsLabelled => Orientation == Orientation.Horizontal && !string.IsNullOrEmpty(Label);

    private static DependencyProperty Register(string name, System.Type type, object fallback) =>
        DependencyProperty.Register(name, type, typeof(OuroSeparator), new PropertyMetadata(fallback, (d, _) => ((OuroSeparator)d).Refresh()));

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        root = GetTemplateChild("Root") as Grid;
        start = GetTemplateChild("LineStart") as Shapes.Rectangle;
        end = GetTemplateChild("LineEnd") as Shapes.Rectangle;
        words = GetTemplateChild("LabelText") as TextBlock;
        Refresh();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroSeparatorAutomationPeer(this);

    private void Refresh()
    {
        var p = OuroUI.SurfacePaint(this);
        var toned = Tone != OuroTone.Secondary && !OuroUI.HighContrast;
        var tone = OuroUI.Source(this, Tone).Tone;
        var line = toned ? tone : p.Line;
        var ink = toned ? OuroPaint.Readable(tone, p.Behind, p.Text) : p.Muted;
        var vertical = Orientation == Orientation.Vertical;
        if (root is not null) root.ColumnSpacing = IsLabelled ? LabelGap : 0;
        if (start is not null)
        {
            start.Fill = OuroUI.Brush(line);
            start.Width = vertical ? LineWidth : double.NaN;
            start.Height = vertical ? double.NaN : LineWidth;
            start.HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            start.VerticalAlignment = vertical ? VerticalAlignment.Stretch : VerticalAlignment.Center;
            Grid.SetColumnSpan(start, IsLabelled ? 1 : 3);
        }
        if (end is not null)
        {
            end.Fill = OuroUI.Brush(line);
            end.Height = LineWidth;
            end.Visibility = IsLabelled ? Visibility.Visible : Visibility.Collapsed;
        }
        if (words is not null)
        {
            words.Text = Label;
            words.Foreground = OuroUI.Brush(ink);
            words.Visibility = IsLabelled ? Visibility.Visible : Visibility.Collapsed;
        }
        MinHeight = vertical ? 16 : IsLabelled ? 0 : LineWidth;
        OuroUI.Report(this, "", "separator", ("labelled", IsLabelled), ("label", OuroPaint.Hex(ink)), ("line", OuroPaint.Hex(line)), ("behind", OuroPaint.Hex(p.Behind)),
            ("trimmed", words?.IsTextTrimmed ?? false), ("textHeight", Math.Round(words?.ActualHeight ?? 0)));
    }
}

/// <summary>A separator to UI Automation: a Separator, or with a label the label as Text.</summary>
public sealed partial class OuroSeparatorAutomationPeer(OuroSeparator owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => owner.IsLabelled ? AutomationControlType.Text : AutomationControlType.Separator;

    protected override string GetClassNameCore() => nameof(OuroSeparator);

    protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : owner.IsLabelled ? owner.Label : "";

    protected override bool IsKeyboardFocusableCore() => false;
}
