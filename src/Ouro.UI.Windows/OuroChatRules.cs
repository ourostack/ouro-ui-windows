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

/// <summary>One line of a message list's slot: its id, who wrote it, when, what, and whether it is the person's own.</summary>
public sealed record OuroListMessage(string Id, string Author, string Time, string Text, bool Mine);

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

    /// <summary>The button a message list shows once the reader scrolls up, and the one above the first message: their smallest sides, in effective pixels.</summary>
    public const double JumpMinSize = 44, MessageGap = 8;

    /// <summary>A reader is at the newest message when no more than this many pixels of the list lie below the view.</summary>
    public const double AtBottomSlack = 24;

    /// <summary>
    /// Reads a message list's slot: lines of "id: author | time | text | mine" separated by semicolons or new lines. A line with no id
    /// and colon is left out, a missing author or time is empty, and "mine" marks the person's own.
    /// </summary>
    public static IReadOnlyList<OuroListMessage> ParseMessages(string? slot)
    {
        var list = new List<OuroListMessage>();
        foreach (var line in (slot ?? "").Split(';', '\n', '\r'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var id = line[..colon].Trim();
            var f = line[(colon + 1)..].Split('|').Select(x => x.Trim()).ToArray();
            if (id.Length == 0) continue;
            list.Add(new OuroListMessage(id, f[0], f.Length > 1 ? f[1] : "", f.Length > 2 ? f[2] : "", f.Length > 3 && f[3].Equals("mine", StringComparison.OrdinalIgnoreCase)));
        }
        return list;
    }

    /// <summary>
    /// The messages to announce when a list changes from <paramref name="before"/> to <paramref name="now"/>: only those added after
    /// the old newest one and written by someone else. A list that started empty, and older messages loaded above, announce nothing.
    /// </summary>
    public static IReadOnlyList<OuroListMessage> ToAnnounce(IReadOnlyList<OuroListMessage> before, IReadOnlyList<OuroListMessage> now)
    {
        if (before.Count == 0) return [];
        var last = before[^1].Id;
        var at = -1;
        for (var i = 0; i < now.Count; i++)
            if (now[i].Id == last) at = i;
        if (at < 0) return [];
        return now.Skip(at + 1).Where(m => !m.Mine && !before.Any(b => b.Id == m.Id)).ToList();
    }

    /// <summary>What a new message is announced as: "author: text".</summary>
    public static string Announcement(OuroListMessage m) => $"{m.Author}: {m.Text}";

    /// <summary>Whether a reader at <paramref name="offset"/> in a view with <paramref name="extent"/> of scrollable height is at the newest message.</summary>
    public static bool AtBottom(double offset, double extent) => extent - offset <= AtBottomSlack;

    /// <summary>The Composer's default row limit before its box scrolls.</summary>
    public const int DefaultMaxRows = 10;

    /// <summary>"modenter" when asked for, otherwise "enter": the two ways a Composer sends.</summary>
    public static string NormalizeSendOn(string? sendOn) => string.Equals(sendOn?.Trim(), "modenter", StringComparison.OrdinalIgnoreCase) ? "modenter" : "enter";

    /// <summary>Whether Enter with these modifiers is the send key: Enter alone sends (Shift+Enter is a newline), and with "modenter" only Ctrl+Enter sends.</summary>
    public static bool IsSendKey(bool shift, bool ctrl, string sendOn) => !shift && (NormalizeSendOn(sendOn) == "modenter" ? ctrl : true);

    /// <summary>Whether the Composer may send now: never while an input method is composing, while sending, with a reason set, disabled, or with nothing written.</summary>
    public static bool CanSend(string text, bool composing, bool sending, bool hasReason, bool disabled) =>
        !composing && !sending && !hasReason && !disabled && !string.IsNullOrWhiteSpace(text);

    /// <summary>The tallest the Composer's box grows, in epx, before it scrolls: <paramref name="maxRows"/> lines plus its padding.</summary>
    public static double BoxMaxHeight(int maxRows, double lineHeight, double padding) => Math.Max(1, maxRows) * lineHeight + padding;
}
