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

    [Fact]
    public void Actions_are_the_trimmed_labels_in_order()
    {
        Assert.Equal(["Delete", "Archive"], OuroListRules.Actions(" Delete , Archive,, "));
        Assert.Empty(OuroListRules.Actions(null));
    }

    [Fact]
    public void The_actions_button_is_named_by_the_app_or_by_the_row()
    {
        Assert.Equal("Actions for Lunch plans", OuroListRules.ActionsButtonName("Lunch plans", null));
        Assert.Equal("Actions for Lunch plans", OuroListRules.ActionsButtonName("Lunch plans", ""));
        Assert.Equal("More for lunch", OuroListRules.ActionsButtonName("Lunch plans", "More for lunch"));
    }

    [Theory]
    [InlineData(2, 5, OuroMove.Up, 1)]
    [InlineData(0, 5, OuroMove.Up, 0)]
    [InlineData(2, 5, OuroMove.Down, 3)]
    [InlineData(4, 5, OuroMove.Down, 4)]
    [InlineData(3, 5, OuroMove.Top, 0)]
    [InlineData(1, 5, OuroMove.Bottom, 4)]
    [InlineData(0, 0, OuroMove.Bottom, 0)]
    public void A_move_lands_in_place_and_stops_at_the_ends(int from, int count, OuroMove move, int to) => Assert.Equal(to, OuroListRules.MoveTarget(from, count, move));

    [Fact]
    public void A_reason_alone_locks_the_order_and_names_each_handle()
    {
        Assert.False(OuroListRules.Locked(false, null));
        Assert.True(OuroListRules.Locked(true, null));
        Assert.True(OuroListRules.Locked(false, "Sorted by date"));
        Assert.Equal("Reorder Intro", OuroListRules.HandleName("Intro", null));
        Assert.Equal("Reorder Intro, Sorted by date", OuroListRules.HandleName("Intro", "Sorted by date"));
        Assert.Equal("Position 2 of 5", OuroListRules.Position(1, 5));
    }

    [Fact]
    public void A_conversation_is_named_by_title_unread_time_preview_and_current_in_that_order()
    {
        Assert.Equal("Ari, Unread, 9:41, Lunch at noon?", OuroListRules.ConversationName("Ari", true, null, "9:41", "Lunch at noon?", false));
        Assert.Equal("Sam, Mon, Notes from Monday, current", OuroListRules.ConversationName("Sam", false, null, "Mon", "Notes from Monday", true));
        Assert.Equal("Sam, New", OuroListRules.ConversationName("Sam", true, "New", "", null, false));
        Assert.Equal("More options for Ari", OuroListRules.MoreName("Ari", null));
        Assert.Equal("More about Ari", OuroListRules.MoreName("Ari", "More about"));
        Assert.Equal("Nothing here yet", OuroListRules.EmptyText(""));
        Assert.Equal("No conversations yet", OuroListRules.EmptyText("No conversations yet"));
    }
}
