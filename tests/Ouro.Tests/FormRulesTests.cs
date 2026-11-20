using Ouro.UI.Windows;
using Xunit;

namespace Ouro.Tests;

public class FormRulesTests
{
    [Theory]
    [InlineData("Course", true, "Course, required")]
    [InlineData("Course", false, "Course")]
    public void TheSpokenNameSaysRequired(string label, bool required, string spoken) => Assert.Equal(spoken, OuroFormRules.Name(label, required));

    [Theory]
    [InlineData("Main is full.", "Pick one.", "Main is full. Pick one.")]
    [InlineData("", "At 2 a.m.", "At 2 a.m.")]
    [InlineData("Enter an email address.", null, "Enter an email address.")]
    [InlineData(null, null, "")]
    public void HelpIsTheErrorThenTheDescription(string? error, string? description, string help) => Assert.Equal(help, OuroFormRules.Help(error, description));

    [Theory]
    [InlineData(-1, 2, 1, 0)]
    [InlineData(-1, 2, -1, 1)]
    [InlineData(0, 2, 1, 1)]
    [InlineData(1, 2, 1, 1)]
    [InlineData(0, 2, -1, 0)]
    [InlineData(-1, 0, 1, -1)]
    public void ArrowsStopAtTheEnds(int index, int count, int delta, int expected) => Assert.Equal(expected, OuroFormRules.Move(index, count, delta));

    [Fact]
    public void AClosedSelectShowsTheChoiceElseThePlaceholder()
    {
        Assert.Equal("Main", OuroFormRules.Shown("Main", "Choose a course"));
        Assert.Equal("Choose a course", OuroFormRules.Shown(null, "Choose a course"));
    }
}
