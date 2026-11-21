// The rules the list components share, as plain values so they are tested off Windows too (core's list specs, vendor/core/components/item-group.spec.json,
// swipe-actions.spec.json, reorderable.spec.json and conversation-list.spec.json). The controls turn the answers into names, lines and focus.
namespace Ouro.UI.Windows;

/// <summary>How an item group draws its rows.</summary>
public enum OuroItemGroupVariant
{
    /// <summary>A line between rows, the default.</summary>
    Divided,
    /// <summary>No lines.</summary>
    Plain,
    /// <summary>The whole list in one bordered box, with a line between rows.</summary>
    Outline,
}

/// <summary>A move a person can make to a reorderable row.</summary>
public enum OuroMove
{
    /// <summary>One place toward the start.</summary>
    Up,
    /// <summary>One place toward the end.</summary>
    Down,
    /// <summary>To the start.</summary>
    Top,
    /// <summary>To the end.</summary>
    Bottom,
}

/// <summary>What a list says and draws, as plain numbers and words.</summary>
public static class OuroListRules
{
    /// <summary>A row's spoken name: its words, and for an ordered list its place, such as "Lemon orzo, 2 of 3". Places count from 1.</summary>
    public static string RowName(string words, int index, int count, bool ordered) => ordered ? $"{words}, {index + 1} of {count}" : words;

    /// <summary>Whether a line is drawn above the row at <paramref name="index"/>: between rows in the divided and outline variants, never above the first.</summary>
    public static bool LineAbove(OuroItemGroupVariant variant, int index) => index > 0 && variant != OuroItemGroupVariant.Plain;

    /// <summary>
    /// The row that takes focus after the focused one at <paramref name="removed"/> leaves a list that now has <paramref name="count"/> rows: the row that took its
    /// place, or the one before it when it was last, or -1 when none is left.
    /// </summary>
    public static int FocusAfterRemoval(int removed, int count) => count <= 0 ? -1 : Math.Min(Math.Max(removed, 0), count - 1);

    /// <summary>The actions a list of labels names, such as "Delete, Archive": each label trimmed, in order, with empty ones left out.</summary>
    public static IReadOnlyList<string> Actions(string? labels) =>
        (labels ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The name of a row's actions button: the name the app gave it, else "Actions for {label}".</summary>
    public static string ActionsButtonName(string label, string? actionsLabel) => string.IsNullOrEmpty(actionsLabel) ? $"Actions for {label}" : actionsLabel;

    /// <summary>Where the row at <paramref name="from"/> in a list of <paramref name="count"/> rows lands after <paramref name="move"/>; a move off either end stays where it is.</summary>
    public static int MoveTarget(int from, int count, OuroMove move) => count <= 0 ? from : move switch
    {
        OuroMove.Up => Math.Max(from - 1, 0),
        OuroMove.Down => Math.Min(from + 1, count - 1),
        OuroMove.Top => 0,
        _ => count - 1,
    };

    /// <summary>Whether the order is locked: <paramref name="disabled"/>, or any reason given, which alone locks it.</summary>
    public static bool Locked(bool disabled, string? reason) => disabled || !string.IsNullOrEmpty(reason);

    /// <summary>A row's handle name, "Reorder Intro", with the reason the order is locked after it when there is one.</summary>
    public static string HandleName(string row, string? reason) => string.IsNullOrEmpty(reason) ? $"Reorder {row}" : $"Reorder {row}, {reason}";

    /// <summary>A row's place as it is announced, "Position 2 of 5". Places count from 1.</summary>
    public static string Position(int index, int count) => $"Position {index + 1} of {count}";

    /// <summary>A conversation row's name: its title, then the unread label when it has new messages, then its time and preview, then "current" for the open one.</summary>
    public static string ConversationName(string title, bool unread, string? unreadLabel, string? time, string? preview, bool current)
    {
        var parts = new List<string> { title };
        if (unread) parts.Add(string.IsNullOrEmpty(unreadLabel) ? "Unread" : unreadLabel);
        if (!string.IsNullOrEmpty(time)) parts.Add(time);
        if (!string.IsNullOrEmpty(preview)) parts.Add(preview);
        if (current) parts.Add("current");
        return string.Join(", ", parts);
    }

    /// <summary>The name of a conversation's more button: the label (default "More options for"), then the row's title.</summary>
    public static string MoreName(string title, string? moreLabel) => $"{(string.IsNullOrEmpty(moreLabel) ? "More options for" : moreLabel)} {title}";

    /// <summary>What a list with no conversations shows: the app's words, else "Nothing here yet".</summary>
    public static string EmptyText(string? empty) => string.IsNullOrEmpty(empty) ? "Nothing here yet" : empty;
}
