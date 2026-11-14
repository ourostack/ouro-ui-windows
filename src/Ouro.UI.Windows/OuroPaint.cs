// How a tone and a variant become colours, as plain ARGB numbers so the rules are tested off Windows too (the tests
// run them over every theme core ships). The controls turn the answer into brushes; nothing else picks colours.
//
// The rule that keeps text readable: a label takes the tone's own colour only where that reads at 4.5:1 or better on
// what is behind it, and otherwise the theme's foreground. So no theme, tone or variant can draw unreadable words.
namespace Ouro.UI.Windows;

/// <summary>A colour role from the theme (core's tone property).</summary>
public enum OuroTone
{
    /// <summary>colors.primary.</summary>
    Primary,
    /// <summary>colors.secondary.</summary>
    Secondary,
    /// <summary>colors.destructive.</summary>
    Destructive,
    /// <summary>colors.success.</summary>
    Success,
    /// <summary>colors.warning.</summary>
    Warning,
}

/// <summary>Visual weight (core's variant property). Badges take Solid, Soft and Outline.</summary>
public enum OuroVariant
{
    /// <summary>Filled with the tone.</summary>
    Solid,
    /// <summary>Tinted with the tone.</summary>
    Soft,
    /// <summary>The tone's edge, no fill.</summary>
    Outline,
    /// <summary>Words only.</summary>
    Plain,
}

/// <summary>A tone's colours in one mode, and the surface they sit on, as 0xAARRGGBB.</summary>
public readonly record struct OuroToneSource(uint Tone, uint ToneForeground, uint Background, uint Foreground);

/// <summary>What a control draws: its fill (0 for none), its words, and its edge (0 for none); and the hover and pressed fills.</summary>
public readonly record struct OuroPaintResult(uint Fill, uint Label, uint Edge, uint Hover, uint Pressed)
{
    /// <summary>What the words sit on: the fill, or the surface when there is none.</summary>
    public uint Behind(uint background) => Fill == 0 ? background : Fill;
}

/// <summary>The colour rules for a tone and a variant.</summary>
public static class OuroPaint
{
    /// <summary>WCAG's minimum for body text.</summary>
    public const double MinContrast = 4.5;

    /// <summary>How much of the tone a soft fill takes, over the surface.</summary>
    public const double SoftTint = 0.16;

    /// <summary>How much a hover, then a press, moves a fill toward the words' colour: "a step stronger".</summary>
    public const double HoverStep = 0.08, PressedStep = 0.16;

    /// <summary>The colours for a tone and a variant over a surface.</summary>
    public static OuroPaintResult Resolve(OuroToneSource s, OuroVariant variant)
    {
        uint fill, label, edge;
        switch (variant)
        {
            case OuroVariant.Solid:
                fill = s.Tone;
                label = s.ToneForeground;
                edge = s.Tone;
                break;
            case OuroVariant.Soft:
                fill = Mix(s.Background, s.Tone, SoftTint);
                label = Readable(s.Tone, fill, s.Foreground);
                edge = fill;
                break;
            case OuroVariant.Outline:
                fill = 0;
                label = Readable(s.Tone, s.Background, s.Foreground);
                edge = Readable(s.Tone, s.Background, s.Foreground, 3.0);
                break;
            default:
                fill = 0;
                label = Readable(s.Tone, s.Background, s.Foreground);
                edge = 0;
                break;
        }
        var under = fill == 0 ? s.Background : fill;
        return new OuroPaintResult(fill, label, edge, Mix(under, label, HoverStep), Mix(under, label, PressedStep));
    }

    /// <summary>The tone's colour when it reads at <paramref name="min"/>:1 on the background, else the foreground.</summary>
    public static uint Readable(uint tone, uint background, uint foreground, double min = MinContrast) =>
        Contrast(tone, background) >= min ? tone : foreground;

    /// <summary><paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/>, opaque.</summary>
    public static uint Mix(uint a, uint b, double t)
    {
        static uint Channel(uint x, uint y, int shift, double t) => (uint)Math.Round(((x >> shift) & 0xFF) * (1 - t) + ((y >> shift) & 0xFF) * t) << shift;
        return 0xFF000000 | Channel(a, b, 16, t) | Channel(a, b, 8, t) | Channel(a, b, 0, t);
    }

    /// <summary>WCAG 2 contrast ratio between two opaque colours.</summary>
    public static double Contrast(uint a, uint b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    /// <summary>WCAG 2 relative luminance.</summary>
    public static double Luminance(uint argb)
    {
        static double Linear(uint c)
        {
            var v = c / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear((argb >> 16) & 0xFF) + 0.7152 * Linear((argb >> 8) & 0xFF) + 0.0722 * Linear(argb & 0xFF);
    }

    /// <summary>#AARRGGBB, as the controls report it to UI Automation.</summary>
    public static string Hex(uint argb) => $"#{argb:X8}";
}
