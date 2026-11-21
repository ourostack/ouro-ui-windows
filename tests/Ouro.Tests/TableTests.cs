using Ouro.UI.Windows;
using Xunit;

namespace Ouro.Tests;

public class TableTests
{
    [Fact]
    public void A_sorted_header_says_its_direction_and_the_others_say_nothing()
    {
        Assert.Equal("Serves, sorted descending", OuroTableRules.HeaderName("Serves", true, OuroSortDirection.Descending));
        Assert.Equal("Serves, sorted ascending", OuroTableRules.HeaderName("Serves", true, OuroSortDirection.Ascending));
        Assert.Equal("Recipe", OuroTableRules.HeaderName("Recipe", false, OuroSortDirection.Descending));
    }

    [Fact]
    public void A_row_is_named_by_its_cells_in_order()
    {
        Assert.Equal("Herb salad, 10 min, 2", OuroTableRules.RowName(["Herb salad", "10 min", "2"]));
    }

    [Fact]
    public void A_press_sorts_ascending_then_reverses_and_a_new_column_starts_ascending()
    {
        Assert.Equal(OuroSortDirection.Ascending, OuroTableRules.NextDirection(false, OuroSortDirection.Descending));
        Assert.Equal(OuroSortDirection.Descending, OuroTableRules.NextDirection(true, OuroSortDirection.Ascending));
        Assert.Equal(OuroSortDirection.Ascending, OuroTableRules.NextDirection(true, OuroSortDirection.Descending));
    }

    [Fact]
    public void A_sort_is_announced_in_the_apps_words_or_by_default()
    {
        Assert.Equal("Sorted by Serves, descending", OuroTableRules.SortedAnnouncement("Serves", OuroSortDirection.Descending, null));
        Assert.Equal("Now by servings", OuroTableRules.SortedAnnouncement("Serves", OuroSortDirection.Descending, "Now by servings"));
    }

    [Fact]
    public void An_empty_body_says_the_apps_words_or_nothing_here_yet()
    {
        Assert.Equal("Nothing here yet.", OuroTableRules.EmptyText(null));
        Assert.Equal("No recipes match.", OuroTableRules.EmptyText("No recipes match."));
    }

    [Theory]
    [InlineData(false, false, 40)]
    [InlineData(true, false, 32)]
    [InlineData(true, true, 44)]
    [InlineData(false, true, 44)]
    public void Rows_are_40_tall_32_when_dense_and_44_when_pressed(bool dense, bool pressable, double height) =>
        Assert.Equal(height, OuroTableRules.RowHeight(dense, pressable));

    [Fact]
    public void Stripes_shade_every_other_body_row_and_columns_are_found_by_header()
    {
        Assert.False(OuroTableRules.Shaded(true, 0));
        Assert.True(OuroTableRules.Shaded(true, 1));
        Assert.False(OuroTableRules.Shaded(false, 1));
        Assert.Equal(["Recipe", "Serves"], OuroTableRules.Words(" Recipe , Serves,, "));
        Assert.Equal(2, OuroTableRules.ColumnOf(["Recipe", "Time", "Serves"], "serves"));
        Assert.Equal(-1, OuroTableRules.ColumnOf(["Recipe"], null));
    }

    [Fact]
    public void Rows_written_as_text_are_cut_into_cells_and_padded_to_the_columns()
    {
        var rows = OuroTableRules.ParseRows("Herb salad | 10 min | 2; Rye bread | 18 h;", 3);
        Assert.Equal(2, rows.Count);
        Assert.Equal(["Herb salad", "10 min", "2"], rows[0]);
        Assert.Equal(["Rye bread", "18 h", ""], rows[1]);
        Assert.Empty(OuroTableRules.ParseRows(null, 3));
    }

    [Fact]
    public void Sorting_puts_numbers_by_value_then_words_and_empty_cells_last_either_way()
    {
        string[][] rows = [["b", "10"], ["a", ""], ["c", "9"], ["d", "x"], ["e", "9"]];
        Assert.Equal(["c", "e", "b", "d", "a"], OuroTableRules.Sort(rows, 1, OuroSortDirection.Ascending).Select(r => r[0]));
        Assert.Equal(["d", "b", "c", "e", "a"], OuroTableRules.Sort(rows, 1, OuroSortDirection.Descending).Select(r => r[0]));
    }
}
