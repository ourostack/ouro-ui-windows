// Core's Badge (vendor/core/components/badge.spec.json): a few words in the tone's colours. Words, not a control, so
// UI Automation sees one Text named by them, never keyboard focusable.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Ouro.UI.Windows;

/// <summary>A short label such as Vegan or 3, in a tone's colours.</summary>
public sealed partial class OuroBadge : Control
{
    /// <summary>Core's minimum height and side padding, in effective pixels.</summary>
    public const double MinBadgeHeight = 24, PaddingX = 8;

    private TextBlock? words;

    public static readonly DependencyProperty TextProperty = Register(nameof(Text), typeof(string), "");
    public static readonly DependencyProperty ToneProperty = Register(nameof(Tone), typeof(OuroTone), OuroTone.Secondary);
    public static readonly DependencyProperty VariantProperty = Register(nameof(Variant), typeof(OuroVariant), OuroVariant.Solid);

    public OuroBadge()
    {
        DefaultStyleKey = typeof(OuroBadge);
        IsTabStop = false;
        OuroUI.Follow(this, Refresh);
        SizeChanged += (_, _) => Refresh();
    }

    /// <summary>The words, which are also its spoken name.</summary>
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// <summary>The colour role from the theme.</summary>
    public OuroTone Tone { get => (OuroTone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }

    /// <summary>Solid, soft or outline (a badge has no plain).</summary>
    public OuroVariant Variant { get => (OuroVariant)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }

    private static DependencyProperty Register(string name, Type type, object fallback) =>
        DependencyProperty.Register(name, type, typeof(OuroBadge), new PropertyMetadata(fallback, (d, _) => ((OuroBadge)d).Refresh()));

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        words = GetTemplateChild("Words") as TextBlock;
        Refresh();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroBadgeAutomationPeer(this);

    private void Refresh()
    {
        var variant = Variant == OuroVariant.Plain ? OuroVariant.Outline : Variant;
        var paint = OuroUI.Paint(this, Tone, variant);
        Background = OuroUI.Brush(paint.Fill);
        Foreground = OuroUI.Brush(paint.Label);
        BorderBrush = OuroUI.Brush(paint.Edge);
        BorderThickness = new Thickness(paint.Edge == 0 ? 0 : 1);
        MinHeight = MinBadgeHeight;
        Padding = new Thickness(PaddingX, 2, PaddingX, 2);
        CornerRadius = new CornerRadius(MinBadgeHeight / 2);
        OuroUI.Report(this, "", "badge", ("tone", Tone), ("variant", variant), ("fill", OuroPaint.Hex(paint.Fill)), ("label", OuroPaint.Hex(paint.Label)),
            ("edge", OuroPaint.Hex(paint.Edge)), ("behind", OuroPaint.Hex(paint.Behind(OuroUI.Surface(this)))), ("trimmed", words?.IsTextTrimmed ?? false), ("textHeight", Math.Round(words?.ActualHeight ?? 0)));
    }
}

/// <summary>A badge to UI Automation: one Text named by its words.</summary>
public sealed partial class OuroBadgeAutomationPeer(OuroBadge owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

    protected override string GetClassNameCore() => nameof(OuroBadge);

    protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : owner.Text;

    protected override bool IsKeyboardFocusableCore() => false;
}
