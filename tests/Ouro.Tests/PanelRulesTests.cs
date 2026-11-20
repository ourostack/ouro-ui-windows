using Ouro.Actions;
using Ouro.UI.Windows;
using OuroShop.Actions;
using Xunit;

namespace Ouro.Tests;

public class PanelRulesTests
{
    [Theory]
    [InlineData(OuroDialogSize.Small, 384)]
    [InlineData(OuroDialogSize.Medium, 448)]
    [InlineData(OuroDialogSize.Large, 512)]
    [InlineData(OuroDialogSize.ExtraLarge, 672)]
    [InlineData(OuroDialogSize.Auto, 512)]
    public void ADialogsWidthFollowsItsSize(OuroDialogSize size, double width) => Assert.Equal(width, OuroPanelRules.MaxWidth(size));

    [Fact]
    public void FullHasNoMaximum() => Assert.True(double.IsPositiveInfinity(OuroPanelRules.MaxWidth(OuroDialogSize.Full)));

    [Theory]
    [InlineData(null, true, true)]
    [InlineData(null, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    public void OutsidePressDefaultsToModal(bool? asked, bool modal, bool closes) => Assert.Equal(closes, OuroPanelRules.ClosesOnOutsidePress(asked, modal));

    [Fact]
    public void AnOutsidePressNeverClosesAPanelThatIgnoresIt()
    {
        Assert.False(OuroPanelRules.MayClose(OuroCloseReason.OutsidePress, false));
        Assert.True(OuroPanelRules.MayClose(OuroCloseReason.OutsidePress, true));
        Assert.True(OuroPanelRules.MayClose(OuroCloseReason.Escape, false));
        Assert.True(OuroPanelRules.MayClose(OuroCloseReason.Button, false));
    }

    [Fact]
    public void AConfirmDialogAsksTheActionsConfirmText()
    {
        var share = OuroActions.Catalog.Find("share_shopping_list")!;
        Assert.Equal("Share your shopping list with someone in your household.", OuroPanelRules.Question(OuroActions.Catalog.ConfirmText(share), "ignored"));
        Assert.Equal("Sure?", OuroPanelRules.Question(null, "Sure?"));
    }
}
