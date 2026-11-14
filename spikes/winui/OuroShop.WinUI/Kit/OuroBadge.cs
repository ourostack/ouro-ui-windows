// Ouro HX's Badge on Windows: a short label or a count beside something else ("Draft", "3"), web's `Badge` and
// Apple's `OuroBadge`. Like Apple's, it is a label, not a control: UI Automation reads it as one Text element named by
// its words, never as a button, and its icon is decorative. For a chip someone presses, use an OuroButton.
// It renders core's Badge claims: its name is its words (`name.spoken`), it wraps instead of cutting words off at any
// text size (`text.reflows`), and it draws the theme's foreground pair (`contrast.text`; the system's own colours
// under a contrast theme). Only the solid variant in the primary and secondary tones for now: those are the only
// colours the token stub carries, and it must not grow by hand (OuroTokens.stub.cs).
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Ouro.UI.WinUI;

/// <summary>A badge's colour role, named after its token. Web's default is <see cref="Secondary"/>, a quiet neutral label.</summary>
public enum OuroBadgeTone { Primary, Secondary }

public sealed class OuroBadge : Grid
{
    /// <summary>Web's <c>h-6</c>: 24 epx at the default text size, taller as the text grows.</summary>
    public const double MinimumHeight = 24;
    /// <summary>Web's <c>px-2</c>.</summary>
    public const double HorizontalPadding = 8;
    /// <summary>Web's <c>rounded-md</c>: 0.8 of the theme's base radius, as Apple's <c>OuroRadius.md</c>.</summary>
    public static double Radius => OuroTokens.Radius * 0.8;

    private static readonly UISettings Settings = new();
    private readonly TextBlock label;

    public string Text { get; }
    public OuroBadgeTone Tone { get; }

    /// <param name="text">The label: a short word, or a count.</param>
    /// <param name="automationId">The badge's UI Automation id.</param>
    /// <param name="glyph">An optional Segoe Fluent Icons glyph before the label; decorative, so hidden from UI Automation.</param>
    public OuroBadge(string text, string automationId, OuroBadgeTone tone = OuroBadgeTone.Secondary, string? glyph = null)
    {
        Text = text;
        Tone = tone;
        MinHeight = MinimumHeight;
        MaxWidth = 220;
        Padding = new Thickness(HorizontalPadding, 3, HorizontalPadding, 3);
        CornerRadius = new CornerRadius(Radius);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnSpacing = glyph is null ? 0 : 4;

        label = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, IsTextScaleFactorEnabled = true };
        SetColumn(label, 1);
        if (glyph is not null)
        {
            var icon = new FontIcon { Glyph = glyph, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
            Children.Add(icon);
        }
        Children.Add(label);

        AutomationProperties.SetName(this, text);
        AutomationProperties.SetAutomationId(this, automationId);
        if (!new AccessibilitySettings().HighContrast)
        {
            var (bg, fg) = Colors(ActualTheme == ElementTheme.Dark || Application.Current.RequestedTheme == ApplicationTheme.Dark ? OuroTokens.Dark : OuroTokens.Light, tone);
            Background = new SolidColorBrush(bg);
            label.Foreground = new SolidColorBrush(fg);
            foreach (var child in Children) if (child is FontIcon i) i.Foreground = new SolidColorBrush(fg);
        }
        Loaded += (_, _) => Report();
        SizeChanged += (_, _) => Report();
    }

    /// <summary>The fill and the words' colour for a tone: the theme's foreground pair, as web's solid badge.</summary>
    public static (Color Fill, Color Label) Colors(OuroColors c, OuroBadgeTone tone) =>
        tone == OuroBadgeTone.Primary ? (c.Primary, c.PrimaryForeground) : (c.Secondary, c.SecondaryForeground);

    protected override AutomationPeer OnCreateAutomationPeer() => new OuroBadgePeer(this);

    /// <summary>What the claims tests read through UI Automation: the text scale, whether the words are cut off, and the colours drawn.</summary>
    private void Report()
    {
        static string Hex(Brush? b) => b is SolidColorBrush s ? $"#{s.Color.R:x2}{s.Color.G:x2}{s.Color.B:x2}" : "system";
        AutomationProperties.SetItemStatus(this,
            $"tone={Tone.ToString().ToLowerInvariant()};textScale={Settings.TextScaleFactor:0.00};trimmed={label.IsTextTrimmed.ToString().ToLowerInvariant()};labelHeight={label.ActualHeight:0};fill={Hex(Background)};label={Hex(label.Foreground)}");
    }

    /// <summary>One Text element named by the badge's words, with no children, so the icon is never read: Apple's <c>.combine</c>.</summary>
    private sealed class OuroBadgePeer(OuroBadge owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetClassNameCore() => nameof(OuroBadge);
        protected override string GetNameCore() => owner.Text;
        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => true;
        protected override bool IsKeyboardFocusableCore() => false;
        protected override IList<AutomationPeer> GetChildrenCore() => new List<AutomationPeer>();
    }
}
