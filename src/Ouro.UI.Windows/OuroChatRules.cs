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
}
