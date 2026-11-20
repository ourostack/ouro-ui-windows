// Which tab a key moves to, as plain numbers so the rule is tested off Windows too (core's Tabs spec,
// vendor/core/components/tabs.spec.json, accessibility.keyboard: the arrow keys that follow the orientation move to the
// next enabled tab, and Home and End go to the first and last). The controls turn the answer into focus and selection.
namespace Ouro.UI.Windows;

/// <summary>A key that moves between tabs.</summary>
public enum OuroNavKey
{
    /// <summary>The arrow that goes back: Left when horizontal, Up when vertical.</summary>
    Previous,
    /// <summary>The arrow that goes forward: Right when horizontal, Down when vertical.</summary>
    Next,
    /// <summary>Home.</summary>
    First,
    /// <summary>End.</summary>
    Last,
}

/// <summary>Where a key moves among tabs, skipping disabled ones.</summary>
public static class OuroNav
{
    /// <summary>
    /// The index a key moves to from <paramref name="from"/>, or -1 when no tab is enabled. Next and Previous wrap round the
    /// ends, as the WAI-ARIA tabs pattern does; a disabled tab is never the answer.
    /// </summary>
    public static int Move(IReadOnlyList<bool> enabled, int from, OuroNavKey key)
    {
        var n = enabled.Count;
        for (var step = 1; step <= n; step++)
        {
            var i = key switch
            {
                OuroNavKey.First => step - 1,
                OuroNavKey.Last => n - step,
                OuroNavKey.Next => ((from + step) % n + n) % n,
                _ => ((from - step) % n + n) % n,
            };
            if (enabled[i]) return i;
        }
        return -1;
    }

    /// <summary>The accent wash behind the current item of a tab bar: the accent at 14% over what is behind it.</summary>
    public static uint Wash(uint behind, uint accent) => OuroPaint.Mix(behind, accent, 0.14);

    /// <summary>The words of the current item: the accent where it reads at 4.5:1 on the wash, otherwise the theme's foreground.</summary>
    public static uint CurrentWords(uint accent, uint wash, uint foreground) => OuroPaint.Readable(accent, wash, foreground);

    /// <summary>Core's sidebar widths, in effective pixels: open, and collapsed to a rail of icons.</summary>
    public const double SidebarWidth = 256, RailWidth = 64;

    /// <summary>What a screen reader says for a sidebar item: its label and then its badge ("Inbox 12"), or the label alone on the rail.</summary>
    public static string SpokenName(string label, string badge, bool collapsed) =>
        collapsed || string.IsNullOrEmpty(badge) ? label : $"{label} {badge}";
}
