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
}
