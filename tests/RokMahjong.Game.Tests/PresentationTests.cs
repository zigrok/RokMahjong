using Microsoft.Xna.Framework;
using RokMahjong.Core;
using RokMahjong.Game;

namespace RokMahjong.Game.Tests;

public class PresentationTests
{
    [Theory]
    [InlineData(390, 844, "pagoda144")]
    [InlineData(844, 390, "turtle144")]
    [InlineData(390, 844, "turtle144")]
    [InlineData(844, 390, "pagoda144")]
    [InlineData(1280, 800, "turtle144")]
    public void Board_fits_the_available_rectangle(int width, int height, string layoutId)
    {
        var available = new Rectangle(12, 80, width - 24, height - 160);
        var presentation = new BoardPresentation(Layouts.GetLayout(layoutId), available);
        Assert.True(presentation.TileWidth >= 14);
        Assert.True(presentation.PanelRect.Left >= available.Left - 1);
        Assert.True(presentation.PanelRect.Top >= available.Top - 1);
        Assert.True(presentation.PanelRect.Right <= available.Right + 1);
        Assert.True(presentation.PanelRect.Bottom <= available.Bottom + 1);
    }

    [Fact]
    public void Portrait_and_landscape_produce_different_fits_but_both_are_usable()
    {
        var layout = Layouts.GetLayout("pagoda144");
        var portrait = new BoardPresentation(layout, new Rectangle(0, 0, 390, 700));
        var landscape = new BoardPresentation(layout, new Rectangle(0, 0, 900, 420));
        Assert.True(portrait.TileWidth > 0);
        Assert.True(landscape.TileWidth > 0);
        Assert.NotEqual(portrait.TileWidth, landscape.TileWidth);
    }
}
