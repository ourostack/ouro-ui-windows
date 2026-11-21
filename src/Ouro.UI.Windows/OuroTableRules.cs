// The rules Table shares with core's spec (vendor/core/components/table.spec.json), as plain values so they are tested off Windows too.
// The control turns the answers into UI Automation names, rows and sorting.
using System.Globalization;

namespace Ouro.UI.Windows;

/// <summary>Which way a sorted column runs.</summary>
public enum OuroSortDirection
{
    /// <summary>Lowest first, the default.</summary>
    Ascending,
    /// <summary>Highest first.</summary>
    Descending,
}

/// <summary>What a table says and draws, as plain numbers and words.</summary>
public static class OuroTableRules
{
    /// <summary>A header's spoken name: its words, with ", sorted ascending" or ", sorted descending" when it is the sorted column.</summary>
    public static string HeaderName(string header, bool sorted, OuroSortDirection direction) =>
        sorted ? $"{header}, sorted {(direction == OuroSortDirection.Descending ? "descending" : "ascending")}" : header;

    /// <summary>A row's spoken name: its cells in order, joined by commas, such as "Herb salad, 10 min, 2".</summary>
    public static string RowName(IEnumerable<string> cells) => string.Join(", ", cells);

    /// <summary>The words of a list a person typed with commas, trimmed, in order, with empty ones left out.</summary>
    public static IReadOnlyList<string> Words(string? list) =>
        (list ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The direction after a press on a header: ascending the first time, then the other way each time, and ascending again on another column.</summary>
    public static OuroSortDirection NextDirection(bool sorted, OuroSortDirection current) =>
        sorted && current == OuroSortDirection.Ascending ? OuroSortDirection.Descending : OuroSortDirection.Ascending;

    /// <summary>What a screen reader hears after a sort: the app's words, else "Sorted by Serves, descending".</summary>
    public static string SortedAnnouncement(string header, OuroSortDirection direction, string? sortedLabel) =>
        string.IsNullOrEmpty(sortedLabel) ? $"Sorted by {header}, {(direction == OuroSortDirection.Descending ? "descending" : "ascending")}" : sortedLabel;

    /// <summary>What the body shows when there are no rows: the app's words, else "Nothing here yet.".</summary>
    public static string EmptyText(string? empty) => string.IsNullOrEmpty(empty) ? "Nothing here yet." : empty;

    /// <summary>A row's height in effective pixels: 40, 32 when dense, and never under 44 for a row or header a person presses.</summary>
    public static double RowHeight(bool dense, bool pressable) => pressable ? 44 : dense ? 32 : 40;

    /// <summary>Whether the body row at <paramref name="index"/> is shaded: every other one, starting with the second.</summary>
    public static bool Shaded(bool striped, int index) => striped && index % 2 == 1;

    /// <summary>The place of a header among the columns, or -1 when the table has no such column.</summary>
    public static int ColumnOf(IReadOnlyList<string> columns, string? header) =>
        string.IsNullOrEmpty(header) ? -1 : columns.ToList().FindIndex(c => string.Equals(c, header, StringComparison.OrdinalIgnoreCase));

    /// <summary>The rows of a table written as text: rows apart by semicolons, cells by bars, each trimmed, every row padded with empty cells to <paramref name="columns"/>.</summary>
    public static IReadOnlyList<string[]> ParseRows(string? rows, int columns) =>
        (rows ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(r => Fit(r.Split('|', StringSplitOptions.TrimEntries), columns)).ToList();

    /// <summary>A row of cells cut or padded with empty cells to exactly <paramref name="columns"/>.</summary>
    public static string[] Fit(IReadOnlyList<string> cells, int columns) =>
        Enumerable.Range(0, columns).Select(i => i < cells.Count ? cells[i] : "").ToArray();

    /// <summary>
    /// The rows sorted by one column: numbers by value and before words, words without regard to case, and rows with an empty cell last
    /// whichever way it runs. Rows that tie keep the order they came in.
    /// </summary>
    public static IReadOnlyList<string[]> Sort(IReadOnlyList<string[]> rows, int column, OuroSortDirection direction)
    {
        string Cell(string[] r) => column >= 0 && column < r.Length ? r[column] : "";
        var filled = rows.Where(r => Cell(r).Length > 0);
        var sorted = direction == OuroSortDirection.Descending ? filled.OrderByDescending(Cell, Comparer) : filled.OrderBy(Cell, Comparer);
        return sorted.Concat(rows.Where(r => Cell(r).Length == 0)).ToList();
    }

    private static readonly IComparer<string> Comparer = Comparer<string>.Create((a, b) =>
    {
        var an = double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var x);
        var bn = double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var y);
        if (an && bn) return x.CompareTo(y);
        if (an != bn) return an ? -1 : 1;
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    });
}
