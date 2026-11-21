// The rules the chat components share, as plain values so they are tested off Windows too (core's chat specs,
// vendor/core/components/message.spec.json, reaction-bar.spec.json, message-list.spec.json and composer.spec.json).
// The controls turn the answers into names, sizes and announcements.
namespace Ouro.UI.Windows;

/// <summary>Who wrote a message: <see cref="Assistant"/> and <see cref="User"/> sit on opposite sides, <see cref="System"/> is a centred note.</summary>
public enum OuroMessageRole
{
    /// <summary>Someone else's message, at the start.</summary>
    Assistant,
    /// <summary>The person's own message, at the end.</summary>
    User,
    /// <summary>A note from the app, centred with no bubble.</summary>
    System,
}

/// <summary>One reaction on a message: what it is, how many people used it and whether one of them is the person.</summary>
public sealed record OuroReaction(string Key, string Emoji, string Name, int Count, bool Mine);

/// <summary>What the chat components say and measure, as plain words and numbers.</summary>
public static class OuroChatRules
{
    /// <summary>Core's message metrics, in effective pixels: the bubble's padding, and how much of the row a bubble may take.</summary>
    public const double BubblePaddingX = 12, BubblePaddingY = 8, BubbleMaxWidthFraction = 0.8;

    /// <summary>
    /// A message's spoken name: author, time, text and, when it has not gone through, its state ("Not sent", "Sending"), each
    /// left out when empty and joined with commas, such as "You, 9:42, On my way., Not sent".
    /// </summary>
    public static string MessageName(string author, string? time, string text, bool failed, bool pending)
    {
        var parts = new List<string>();
        foreach (var part in new[] { author, time ?? "", text, failed ? "Not sent" : pending ? "Sending" : "" })
            if (part.Length > 0) parts.Add(part);
        return string.Join(", ", parts);
    }

    /// <summary>The widest a bubble may be in a row of <paramref name="rowWidth"/>.</summary>
    public static double BubbleMaxWidth(double rowWidth) => Math.Floor(rowWidth * BubbleMaxWidthFraction);

    /// <summary>Core's reaction-bar metrics, in effective pixels: the pill's drawn height, its touch height, the gap between pills and its side padding.</summary>
    public const double PillHeight = 28, PillTouchHeight = 44, PillGap = 4, PillPaddingX = 8;

    /// <summary>
    /// Reads a reactions slot: lines of "key: emoji | name | count | mine" separated by semicolons or new lines. A line with no key
    /// and colon, or with nothing after it, is left out; a missing name is the key, a missing or odd count is 0, and "mine" marks the person's own.
    /// </summary>
    public static IReadOnlyList<OuroReaction> ParseReactions(string? slot)
    {
        var list = new List<OuroReaction>();
        foreach (var line in (slot ?? "").Split(';', '\n', '\r'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim();
            var fields = line[(colon + 1)..].Split('|').Select(f => f.Trim()).ToArray();
            if (key.Length == 0 || fields[0].Length == 0) continue;
            var count = fields.Length > 2 && int.TryParse(fields[2], out var n) && n > 0 ? n : 0;
            list.Add(new OuroReaction(key, fields[0], fields.Length > 1 && fields[1].Length > 0 ? fields[1] : key, count, fields.Length > 3 && fields[3].Equals("mine", StringComparison.OrdinalIgnoreCase)));
        }
        return list;
    }

    /// <summary>A reaction's spoken name: its name and count, such as "Like, 3".</summary>
    public static string ReactionName(string name, int count) => $"{name}, {count}";
}
