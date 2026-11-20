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
}
