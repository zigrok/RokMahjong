using RokMahjong.Core;

namespace RokMahjong.Core.Tests;

public class DealTests
{
    [Fact]
    public void Every_level_deal_is_solvable()
    {
        foreach (var level in Layouts.Levels)
        {
            var layout = Layouts.GetLayout(level.LayoutId);
            var deck = Layouts.GetDeck(level.DeckId);
            var deal = DealGenerator.Generate(layout, deck, level.Seed);
            Assert.True(DealGenerator.Verify(deal.Tiles, deal.Solution), $"Level {level.Index} is not solvable.");
        }
    }

    [Fact]
    public void Many_seeds_remain_solvable()
    {
        foreach (var layout in Layouts.All)
        {
            var deck = Layouts.Decks.First(d => d.TileCount == layout.TileCount);
            for (var seed = 1; seed <= 200; seed++)
            {
                var deal = DealGenerator.Generate(layout, deck, seed);
                Assert.True(DealGenerator.Verify(deal.Tiles, deal.Solution), $"{layout.Id} seed {seed} failed verification.");
            }
        }
    }

    [Fact]
    public void Generated_solution_clears_through_the_game_api()
    {
        foreach (var level in Layouts.Levels)
        {
            var layout = Layouts.GetLayout(level.LayoutId);
            var deck = Layouts.GetDeck(level.DeckId);
            var deal = DealGenerator.Generate(layout, deck, level.Seed);
            var game = new MahjongGame(deal);

            foreach (var (first, second) in deal.Solution)
            {
                Assert.Equal(SelectResult.Selected, game.TrySelect(first));
                Assert.Equal(SelectResult.Matched, game.TrySelect(second));
            }

            Assert.Equal(GameStatus.Won, game.Status);
            Assert.Equal(0, game.RemainingCount);
        }
    }
}
