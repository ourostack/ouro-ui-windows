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
}
