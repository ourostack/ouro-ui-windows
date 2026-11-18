using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Ouro.UI.Windows;
using Xunit;

namespace Ouro.Tests;

public class PaintTests
{
    /// <summary>Every theme's light and dark colours, read from the vendored OuroTokens.cs (it needs Windows to compile).</summary>
    private static Dictionary<string, Dictionary<string, uint>> Modes([CallerFilePath] string here = "")
    {
        var text = File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "Ouro.UI.Windows", "Vendor", "OuroTokens.cs"));
        var modes = new Dictionary<string, Dictionary<string, uint>>();
        foreach (Match theme in Regex.Matches(text, @"public static readonly OuroTheme (\w+) = new\(([\s\S]*?)Radius:"))
            foreach (Match mode in Regex.Matches(theme.Groups[2].Value, @"(Light|Dark): new OuroColors\(([\s\S]*?)\)\s*,\s*(?=Dark:|$)"))
                modes[$"{theme.Groups[1].Value} {mode.Groups[1].Value}"] = Regex.Matches(mode.Groups[2].Value, @"(\w+): Color\.FromArgb\(0x(..), 0x(..), 0x(..), 0x(..)\)")
                    .ToDictionary(m => m.Groups[1].Value, m => Convert.ToUInt32(m.Groups[2].Value + m.Groups[3].Value + m.Groups[4].Value + m.Groups[5].Value, 16));
        return modes;
    }

    [Fact]
    public void Every_tone_and_variant_reads_at_4_5_to_1_in_every_theme_core_ships()
    {
        var modes = Modes();
        Assert.Equal(12, modes.Count);
        var failures = new List<string>();
        foreach (var (name, c) in modes)
            foreach (var tone in Enum.GetValues<OuroTone>())
                foreach (var variant in Enum.GetValues<OuroVariant>())
                {
                    var t = tone.ToString();
                    var s = new OuroToneSource(c[t], c[t + "Foreground"], c["Background"], c["Foreground"]);
                    var p = OuroPaint.Resolve(s, variant);
                    var ratio = OuroPaint.Contrast(p.Label, p.Behind(s.Background));
                    if (ratio < OuroPaint.MinContrast) failures.Add($"{name} {tone} {variant}: {ratio:0.00}:1");
                }
        Assert.Empty(failures);
    }

    [Fact]
    public void Fields_checkboxes_lines_and_lists_read_on_the_page_and_on_a_card_in_every_theme_core_ships()
    {
        var failures = new List<string>();
        foreach (var (name, c) in Modes())
            foreach (var behind in new[] { "Background", "Card" })
            {
                var p = OuroPaint.Surface(new OuroSurfaceSource(c[behind], c["Background"], c["Foreground"], c["MutedForeground"], c["Border"], c["Input"], c["Ring"], c["Primary"], c["PrimaryForeground"], c["Destructive"]));
                foreach (var (what, fg, bg, min) in new[]
                {
                    ("text", p.Text, p.Behind, OuroPaint.MinContrast), ("text in a field", p.Text, p.Field, OuroPaint.MinContrast),
                    ("muted", p.Muted, p.Behind, OuroPaint.MinContrast), ("placeholder", p.Placeholder, p.Field, OuroPaint.MinContrast),
                    ("error", p.Error, p.Behind, OuroPaint.MinContrast), ("edge", p.Edge, p.Behind, OuroPaint.GraphicContrast),
                    ("focus", p.Focus, p.Behind, OuroPaint.GraphicContrast), ("checked", p.Checked, p.Behind, OuroPaint.GraphicContrast),
                    ("tick", p.Tick, p.Checked, OuroPaint.GraphicContrast),
                })
                    if (OuroPaint.Contrast(fg, bg) < min) failures.Add($"{name} on {behind}: {what} {OuroPaint.Contrast(fg, bg):0.00}:1");
                if (p.Line >> 24 != 0xFF) failures.Add($"{name}: the line is still translucent");
            }
        Assert.Empty(failures);
    }

    [Fact]
    public void A_translucent_colour_is_laid_over_the_surface_and_an_edge_that_would_vanish_takes_the_muted_words()
    {
        Assert.Equal(0xFF7F7F7Fu, OuroPaint.Over(0x80000000, 0xFFFFFFFF));
        Assert.Equal(0xFF123456u, OuroPaint.Over(0xFF123456, 0xFFFFFFFF));
        var p = OuroPaint.Surface(new OuroSurfaceSource(0xFFFFFFFF, 0xFFFFFFFF, 0xFF1D1D1F, 0xFF6A6A6F, 0xFFE3E3E8, 0xFFD2D2D7, 0xFF0071E3, 0xFF0071E3, 0xFFFFFFFF, 0xFFD70015));
        Assert.Equal(0xFF6A6A6Fu, p.Edge);
        Assert.Equal(0xFF0071E3u, p.Checked);
    }

    [Fact]
    public void A_label_falls_back_to_the_foreground_where_the_tone_would_not_read()
    {
        const uint white = 0xFFFFFFFF, ink = 0xFF1D1D1F, paleGrey = 0xFFF2F2F5;
        var p = OuroPaint.Resolve(new OuroToneSource(paleGrey, ink, white, ink), OuroVariant.Plain);
        Assert.Equal(ink, p.Label);
        Assert.Equal(0u, p.Fill);
        Assert.Equal(0u, p.Edge);
    }

    [Fact]
    public void Hover_and_press_are_each_a_step_stronger_and_contrast_matches_wcag()
    {
        var p = OuroPaint.Resolve(new OuroToneSource(0xFF0066CC, 0xFFFFFFFF, 0xFFFFFFFF, 0xFF1D1D1F), OuroVariant.Solid);
        Assert.Equal(0xFF0066CCu, p.Fill);
        Assert.True(OuroPaint.Contrast(p.Hover, p.Fill) > 1 && OuroPaint.Contrast(p.Pressed, p.Fill) > OuroPaint.Contrast(p.Hover, p.Fill));
        Assert.Equal(21.0, OuroPaint.Contrast(0xFF000000, 0xFFFFFFFF), 2);
        Assert.Equal("#FF0066CC", OuroPaint.Hex(p.Fill));
    }
}
