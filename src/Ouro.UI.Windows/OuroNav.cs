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
}
