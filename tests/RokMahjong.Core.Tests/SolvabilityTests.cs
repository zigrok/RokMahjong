using RokMahjong.Core;

namespace RokMahjong.Core.Tests;

public class SolvabilityTests
{
    [Fact]
    public void Oracle_solves_a_36_tile_board()
    {
        var game = MahjongGame.Create(Layouts.Lotus36, Layouts.Mini36, 1234);
        var result = BoardSolver.Solve(game, maxNodes: 100_000);
        Assert.Equal(SolverOutcome.Solved, result.Outcome);
    }

    [Fact]
    public void Ten_thousand_small_deals_remain_solvable()
    {
        for (var seed = 1; seed <= 10_000; seed++)
        {
            var deal = DealGenerator.Generate(Layouts.Lotus36, Layouts.Mini36, seed);
            Assert.True(DealGenerator.Verify(deal.Tiles, deal.Solution), $"Seed {seed} failed verification.");
            var game = new MahjongGame(deal);
            foreach (var (first, second) in deal.Solution)
            {
                Assert.Equal(SelectResult.Selected, game.TrySelect(first));
                Assert.Equal(SelectResult.Matched, game.TrySelect(second));
            }
            Assert.Equal(GameStatus.Won, game.Status);
        }
    }

    [Fact]
    public void Shuffle_solution_replays_from_a_fresh_board()
    {
        var game = MahjongGame.Create(Layouts.Lotus36, Layouts.Mini36, 808);
        var shuffle = game.Shuffle(new SeededRandom(17));
        Assert.True(shuffle.Changed);
        Assert.NotNull(shuffle.Solution);
        foreach (var (first, second) in shuffle.Solution!)
        {
            Assert.Equal(SelectResult.Selected, game.TrySelect(first));
            Assert.Equal(SelectResult.Matched, game.TrySelect(second));
        }
        Assert.Equal(GameStatus.Won, game.Status);
    }

    [Fact]
    public void Shuffle_solution_replays_on_a_42_face_board()
    {
        var game = MahjongGame.Create(Layouts.Pagoda144, Layouts.Classic144, 5501);
        var shuffle = game.Shuffle(new SeededRandom(42));
        Assert.True(shuffle.Changed);
        foreach (var (first, second) in shuffle.Solution!)
        {
            Assert.Equal(SelectResult.Selected, game.TrySelect(first));
            Assert.Equal(SelectResult.Matched, game.TrySelect(second));
        }
        Assert.Equal(GameStatus.Won, game.Status);
    }

    [Fact]
    public void Shuffle_solution_replays_from_a_mid_game_state()
    {
        var game = MahjongGame.Create(Layouts.Lotus36, Layouts.Mini36, 909);
        for (var i = 0; i < 5; i++)
        {
            var hint = game.Hint();
            game.TrySelect(hint.FirstTileId);
            game.TrySelect(hint.SecondTileId);
        }

        var shuffle = game.Shuffle(new SeededRandom(23));
        Assert.True(shuffle.Changed);
        foreach (var (first, second) in shuffle.Solution!)
        {
            Assert.Equal(SelectResult.Selected, game.TrySelect(first));
            Assert.Equal(SelectResult.Matched, game.TrySelect(second));
        }
        Assert.Equal(GameStatus.Won, game.Status);
    }

    [Fact]
    public void Hint_and_deadlock_agree_with_legal_pair_enumeration()
    {
        var game = MahjongGame.Create(Layouts.Lotus36, Layouts.Mini36, 77);
        while (game.Status == GameStatus.Playing)
        {
            var free = game.FreeTiles();
            var hasPair = free
                .GroupBy(tile => tile.Face.MatchKey)
                .Any(group => group.Count() >= 2);
            var hint = game.Hint();
            Assert.Equal(hasPair, hint.HasHint);
            if (!hint.HasHint) break;
            game.TrySelect(hint.FirstTileId);
            game.TrySelect(hint.SecondTileId);
        }
        Assert.True(game.Status is GameStatus.Won or GameStatus.Deadlocked);
    }
}
