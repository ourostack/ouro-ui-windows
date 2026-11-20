using Ouro.UI.Windows;
using Xunit;

namespace Ouro.Tests;

public class NavTests
{
    private static readonly bool[] Settings = [true, false, true];

    [Fact]
    public void Next_and_previous_skip_a_disabled_tab()
    {
        Assert.Equal(2, OuroNav.Move(Settings, 0, OuroNavKey.Next));
        Assert.Equal(0, OuroNav.Move(Settings, 2, OuroNavKey.Previous));
    }

    [Fact]
    public void Next_and_previous_wrap_round_the_ends()
    {
        Assert.Equal(0, OuroNav.Move(Settings, 2, OuroNavKey.Next));
        Assert.Equal(2, OuroNav.Move(Settings, 0, OuroNavKey.Previous));
    }

    [Fact]
    public void Home_and_End_go_to_the_first_and_last_enabled_tab()
    {
        bool[] tabs = [false, true, true, false];
        Assert.Equal(1, OuroNav.Move(tabs, 2, OuroNavKey.First));
        Assert.Equal(2, OuroNav.Move(tabs, 1, OuroNavKey.Last));
    }

    [Fact]
    public void A_list_with_nothing_enabled_goes_nowhere()
    {
        Assert.Equal(-1, OuroNav.Move([false, false], 0, OuroNavKey.Next));
        Assert.Equal(-1, OuroNav.Move([], 0, OuroNavKey.First));
    }

    [Fact]
    public void The_only_enabled_tab_stays_put()
    {
        Assert.Equal(1, OuroNav.Move([false, true], 1, OuroNavKey.Next));
    }
}
