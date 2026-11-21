using Ouro.UI.Windows;
using Xunit;

namespace Ouro.Tests;

public class ChatTests
{
    [Theory]
    [InlineData("Ana", "9:41", "See you at noon.", false, false, "Ana, 9:41, See you at noon.")]
    [InlineData("You", "9:42", "On my way.", true, false, "You, 9:42, On my way., Not sent")]
    [InlineData("You", "9:42", "On my way.", false, true, "You, 9:42, On my way., Sending")]
    [InlineData("Ouro", null, "Ana joined the conversation.", false, false, "Ouro, Ana joined the conversation.")]
    public void A_message_is_named_by_author_time_words_and_state(string author, string? time, string text, bool failed, bool pending, string name) =>
        Assert.Equal(name, OuroChatRules.MessageName(author, time, text, failed, pending));

    [Fact]
    public void A_bubble_takes_at_most_four_fifths_of_the_row() => Assert.Equal(400, OuroChatRules.BubbleMaxWidth(500));

    [Fact]
    public void Reactions_are_read_from_lines_of_key_emoji_name_count_and_mine()
    {
        var list = OuroChatRules.ParseReactions("like: 👍 | Like | 3 | mine; heart: ❤️ | Love | 1\nwow: 😮");
        Assert.Equal(new[] { new OuroReaction("like", "👍", "Like", 3, true), new OuroReaction("heart", "❤️", "Love", 1, false), new OuroReaction("wow", "😮", "wow", 0, false) }, list);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no colon here")]
    [InlineData("empty:  | Name | 2")]
    public void A_line_without_a_key_or_an_emoji_is_left_out(string slot) => Assert.Empty(OuroChatRules.ParseReactions(slot));

    [Fact]
    public void A_reaction_is_named_by_its_name_and_count() => Assert.Equal("Like, 3", OuroChatRules.ReactionName("Like", 3));

    private static IReadOnlyList<OuroListMessage> Msgs(string slot) => OuroChatRules.ParseMessages(slot);

    [Fact]
    public void A_message_slot_is_read_line_by_line_and_mine_marks_the_persons_own()
    {
        var list = Msgs("a: Ana | 9:41 | See you at noon.; b: You | 9:42 | On my way. | mine\nnot a line; c: Bo");
        Assert.Equal(3, list.Count);
        Assert.Equal(new OuroListMessage("a", "Ana", "9:41", "See you at noon.", false), list[0]);
        Assert.True(list[1].Mine);
        Assert.Equal(new OuroListMessage("c", "Bo", "", "", false), list[2]);
    }

    [Fact]
    public void Only_messages_added_after_the_newest_from_someone_else_are_announced()
    {
        var before = Msgs("a: Ana | 9:41 | Hi.");
        var now = Msgs("a: Ana | 9:41 | Hi.; b: You | 9:42 | Yo. | mine; c: Ana | 9:43 | Ready?");
        var said = OuroChatRules.ToAnnounce(before, now);
        Assert.Equal(new[] { "c" }, said.Select(m => m.Id));
        Assert.Equal("Ana: Ready?", OuroChatRules.Announcement(said[0]));
    }

    [Fact]
    public void History_at_mount_and_older_messages_loaded_above_are_never_announced()
    {
        var now = Msgs("o: Ana | 8:00 | Earlier.; a: Ana | 9:41 | Hi.");
        Assert.Empty(OuroChatRules.ToAnnounce([], now));
        Assert.Empty(OuroChatRules.ToAnnounce(Msgs("a: Ana | 9:41 | Hi."), now));
        Assert.Empty(OuroChatRules.ToAnnounce(Msgs("a: Ana | 9:41 | Hi."), Msgs("z: Ana | 9:50 | Else.")));
    }

    [Theory]
    [InlineData(500, 500, true)]
    [InlineData(480, 500, true)]
    [InlineData(100, 500, false)]
    public void A_reader_near_the_end_is_at_the_newest(double offset, double extent, bool expected) => Assert.Equal(expected, OuroChatRules.AtBottom(offset, extent));

    [Theory]
    [InlineData(false, false, "enter", true)]
    [InlineData(true, false, "enter", false)]
    [InlineData(false, true, "modenter", true)]
    [InlineData(false, false, "modenter", false)]
    [InlineData(false, false, "anything", true)]
    public void Enter_sends_or_adds_a_line_by_the_composers_mode(bool shift, bool ctrl, string sendOn, bool sends) => Assert.Equal(sends, OuroChatRules.IsSendKey(shift, ctrl, sendOn));

    [Theory]
    [InlineData("Hi", false, false, false, false, true)]
    [InlineData("Hi", true, false, false, false, false)]
    [InlineData("Hi", false, true, false, false, false)]
    [InlineData("Hi", false, false, true, false, false)]
    [InlineData("Hi", false, false, false, true, false)]
    [InlineData("  ", false, false, false, false, false)]
    public void A_composer_sends_only_when_nothing_stops_it(string text, bool composing, bool sending, bool reason, bool disabled, bool expected) =>
        Assert.Equal(expected, OuroChatRules.CanSend(text, composing, sending, reason, disabled));

    [Fact]
    public void The_box_grows_to_its_row_limit()
    {
        Assert.Equal(10 * 21 + 20, OuroChatRules.BoxMaxHeight(OuroChatRules.DefaultMaxRows, 21, 20));
        Assert.Equal(41, OuroChatRules.BoxMaxHeight(0, 21, 20));
    }
}
