using Ouro.UI.Windows;
using Xunit;

namespace Ouro.Tests;

public class ListTests
{
    [Fact]
    public void An_ordered_row_says_its_place_counting_from_one()
    {
        Assert.Equal("Lemon orzo, 2 of 3", OuroListRules.RowName("Lemon orzo", 1, 3, true));
        Assert.Equal("Lemon orzo", OuroListRules.RowName("Lemon orzo", 1, 3, false));
    }

    [Theory]
    [InlineData(OuroItemGroupVariant.Divided, 0, false)]
    [InlineData(OuroItemGroupVariant.Divided, 1, true)]
    [InlineData(OuroItemGroupVariant.Outline, 2, true)]
    [InlineData(OuroItemGroupVariant.Plain, 2, false)]
    public void A_line_sits_between_rows_except_in_the_plain_variant(OuroItemGroupVariant variant, int index, bool line) =>
        Assert.Equal(line, OuroListRules.LineAbove(variant, index));

    [Theory]
    [InlineData(1, 4, 1)]
    [InlineData(4, 4, 3)]
    [InlineData(0, 0, -1)]
    public void Focus_goes_to_the_row_that_took_the_place_or_the_one_before_when_it_was_last(int removed, int count, int next) =>
        Assert.Equal(next, OuroListRules.FocusAfterRemoval(removed, count));
}
