// What every Ouro control shares: the theme it draws from, the person's Windows settings it follows (dark mode, a
// contrast theme, text size, animations), and the self-report the checks read through UI Automation.
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Ouro.UI.Hx.Tokens;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Ouro.UI.Windows;

/// <summary>The kit's settings, and the Windows settings its controls follow.</summary>
public static class OuroUI
{
    private static readonly UISettings Settings = new();
    private static readonly AccessibilitySettings Accessibility = new();
    private static OuroTheme theme = OuroTokens.Theme(OuroTokens.DefaultTheme);

    /// <summary>The theme every control draws from: Pearl unless the app picks another of core's themes.</summary>
    public static OuroTheme Theme
    {
        get => theme;
        set { theme = value; Changed?.Invoke(); }
    }

    /// <summary>
    /// When true, each control writes what it drew (its colours, press scale, text scale and whether its words are cut
    /// off) into its UI Automation ItemStatus, for the claims checks. Off for people: a screen reader would read it.
    /// </summary>
    public static bool SelfReport { get; set; }

    /// <summary>Whether Windows animations are on (Settings, Accessibility, Visual effects, Animation effects).</summary>
    public static bool AnimationsEnabled => Settings.AnimationsEnabled;

    /// <summary>The person's text size (Settings, Accessibility, Text size): 1 to 2.25.</summary>
    public static double TextScale => Settings.TextScaleFactor;

    /// <summary>The text size at which side-by-side parts stack, so words keep their room.</summary>
    public const double StackAtTextScale = 1.5;

    /// <summary>Whether a Windows contrast theme is on. Under one, the controls draw the person's system colours.</summary>
    public static bool HighContrast => Accessibility.HighContrast;

    /// <summary>The scale a press shrinks to: core's press scale, or none when Windows animations are off.</summary>
    public static float PressScale => AnimationsEnabled ? (float)OuroTokens.Press.Scale : 1f;

    internal static event Action? Changed;

    static OuroUI()
    {
        Settings.ColorValuesChanged += (_, _) => Changed?.Invoke();
        Settings.TextScaleFactorChanged += (_, _) => Changed?.Invoke();
        Settings.AnimationsEnabledChanged += (_, _) => Changed?.Invoke();
    }

    /// <summary>The theme's colours for the element's light or dark mode.</summary>
    public static OuroColors Colors(FrameworkElement e) => Theme.Colors(e.ActualTheme == ElementTheme.Dark);

    /// <summary>A tone's colours, and the surface they sit on, for the element's mode.</summary>
    public static OuroToneSource Source(FrameworkElement e, OuroTone tone)
    {
        var c = Colors(e);
        var (t, f) = tone switch
        {
            OuroTone.Secondary => (c.Secondary, c.SecondaryForeground),
            OuroTone.Destructive => (c.Destructive, c.DestructiveForeground),
            OuroTone.Success => (c.Success, c.SuccessForeground),
            OuroTone.Warning => (c.Warning, c.WarningForeground),
            _ => (c.Primary, c.PrimaryForeground),
        };
        return new OuroToneSource(Argb(t), Argb(f), Argb(c.Background), Argb(c.Foreground));
    }

    /// <summary>
    /// The colours under a contrast theme: a solid control takes the highlight pair, anything else the button pair with
    /// a text-coloured edge. Hover and press keep the same pair; WinUI's focus ring and the edge carry the state.
    /// </summary>
    public static OuroPaintResult SystemPaint(OuroVariant variant)
    {
        uint Sys(UIElementType t) => Argb(Settings.UIElementColor(t));
        return variant == OuroVariant.Solid
            ? new OuroPaintResult(Sys(UIElementType.Highlight), Sys(UIElementType.HighlightText), Sys(UIElementType.Highlight), Sys(UIElementType.Highlight), Sys(UIElementType.Highlight))
            : new OuroPaintResult(Sys(UIElementType.ButtonFace), Sys(UIElementType.ButtonText), Sys(UIElementType.ButtonText), Sys(UIElementType.ButtonFace), Sys(UIElementType.ButtonFace));
    }

    /// <summary>A system colour, as 0xAARRGGBB: window, window text, gray text.</summary>
    public static uint System(UIElementType type) => Argb(Settings.UIElementColor(type));

    /// <summary>What the element sits on: the theme's background, or the system window colour under a contrast theme.</summary>
    public static uint Surface(FrameworkElement e) => HighContrast ? System(UIElementType.Window) : Argb(Colors(e).Background);

    /// <summary>Paint for the element: the theme's, or the system's under a contrast theme.</summary>
    public static OuroPaintResult Paint(FrameworkElement e, OuroTone tone, OuroVariant variant) =>
        HighContrast ? SystemPaint(variant) : OuroPaint.Resolve(Source(e, tone), variant);

    /// <summary>0xAARRGGBB of a colour.</summary>
    public static uint Argb(Color c) => (uint)c.A << 24 | (uint)c.R << 16 | (uint)c.G << 8 | c.B;

    /// <summary>A brush for 0xAARRGGBB; 0 is transparent.</summary>
    public static Brush Brush(uint argb) => new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));

    /// <summary>Which of light, dark or a contrast theme the element draws in.</summary>
    public static string Mode(FrameworkElement e) => HighContrast ? "HighContrast" : e.ActualTheme.ToString();

    /// <summary>
    /// Calls <paramref name="refresh"/> when the element loads and whenever the theme, the element's mode, a contrast
    /// theme, the text size or animations change, while it is loaded.
    /// </summary>
    internal static void Follow(FrameworkElement e, Action refresh)
    {
        var queue = DispatcherQueue.GetForCurrentThread();
        void OnChanged() => queue?.TryEnqueue(() => refresh());
        e.Loaded += (_, _) => { Changed += OnChanged; refresh(); };
        e.Unloaded += (_, _) => Changed -= OnChanged;
        e.ActualThemeChanged += (_, _) => refresh();
    }

    /// <summary>Writes the self-report, when it is on, after the control's own status (such as Busy).</summary>
    internal static void Report(FrameworkElement e, string status, string kind, params (string Key, object? Value)[] facts)
    {
        if (!SelfReport)
        {
            AutomationProperties.SetItemStatus(e, status);
            return;
        }
        var report = string.Join(";", facts.Select(f => $"{f.Key}={f.Value switch { bool b => b ? "true" : "false", double d => d.ToString("0.00", global::System.Globalization.CultureInfo.InvariantCulture), float d => d.ToString("0.00", global::System.Globalization.CultureInfo.InvariantCulture), _ => f.Value }}"));
        AutomationProperties.SetItemStatus(e, $"{status}{(status.Length > 0 ? " " : "")}ouro:{kind};mode={Mode(e)};textScale={TextScale.ToString("0.00", global::System.Globalization.CultureInfo.InvariantCulture)};{report}");
    }
}
