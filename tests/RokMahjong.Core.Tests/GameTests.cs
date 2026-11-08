using RokMahjong.Core;

namespace RokMahjong.Core.Tests;

public class GameTests
{
    private static MahjongGame NewGame() => MahjongGame.Create(Layouts.Lotus36, Layouts.Mini36, 1234);

    [Fact]
    public void Matching_a_pair_updates_score_and_history()
    {
        var game = NewGame();
        var pair = game.Hint();
        Assert.True(pair.HasHint);
        Assert.Equal(SelectResult.Selected, game.TrySelect(pair.FirstTileId));
        Assert.Equal(SelectResult.Matched, game.TrySelect(pair.SecondTileId));
        Assert.Equal(34, game.RemainingCount);
        Assert.True(game.Score > 0);
        Assert.True(game.CanUndo);
    }

    [Fact]
    public void Undo_restores_the_exact_state()
    {
        var game = NewGame();
        var before = game.Snapshot();
        var pair = game.Hint();
        game.TrySelect(pair.FirstTileId);
        game.TrySelect(pair.SecondTileId);
        Assert.True(game.Undo());
        var after = game.Snapshot();
        Assert.Equal(before.Score, after.Score);
        Assert.Equal(before.Combo, after.Combo);
        Assert.Equal(before.RemainingCount(), after.RemainingCount());
        Assert.Equal(before.Tiles.Select(t => (t.Id, t.Removed)), after.Tiles.Select(t => (t.Id, t.Removed)));
    }

    [Fact]
    public void Shuffle_keeps_the_same_face_multiset()
    {
        var game = NewGame();
        var before = game.Tiles.Where(t => !t.Removed).Select(t => t.Face).OrderBy(f => f.Label).ToList();
        var result = game.Shuffle(new SeededRandom(99));
        Assert.True(result.Changed);
        var after = game.Tiles.Where(t => !t.Removed).Select(t => t.Face).OrderBy(f => f.Label).ToList();
        Assert.Equal(before, after);
        Assert.NotEqual(GameStatus.Deadlocked, game.Status);
    }

    [Fact]
    public void Deadlock_is_detected_after_a_match_leaves_no_pair()
    {
        var layout = new BoardLayout("deadlock4", "Test", "Deadlock 4", new[]
        {
            new TileCoord(0, 0, 0), new TileCoord(2, 0, 0),
            new TileCoord(0, 2, 0), new TileCoord(2, 2, 0),
        }, false);
        var deck = new DeckProfile("test", new[] { new DeckEntry(new TileFace(MatchGroup.Characters, 1, "C1"), 2) });
        var faces = new[]
        {
            new TileFace(MatchGroup.Characters, 1, "C1"),
            new TileFace(MatchGroup.Characters, 1, "C1"),
            new TileFace(MatchGroup.Bamboo, 2, "B2"),
            new TileFace(MatchGroup.Circles, 3, "D3"),
        };
        var tiles = layout.Slots.Select((coord, index) => new Tile(index, coord, faces[index])).ToList();
        var game = new MahjongGame(new DealResult(layout, deck, 1, tiles, Array.Empty<(int, int)>()));
        Assert.Equal(GameStatus.Playing, game.Status);
        Assert.Equal(SelectResult.Selected, game.TrySelect(0));
        Assert.Equal(SelectResult.Matched, game.TrySelect(1));
        Assert.Equal(GameStatus.Deadlocked, game.Status);
    }

    [Fact]
    public void Snapshot_round_trip_preserves_the_board()
    {
        var game = NewGame();
        var pair = game.Hint();
        game.TrySelect(pair.FirstTileId);
        game.TrySelect(pair.SecondTileId);
        var snapshot = game.Snapshot();
        var restored = MahjongGame.FromSnapshot(snapshot);
        Assert.Equal(game.Layout.Id, restored.Layout.Id);
        Assert.Equal(game.Score, restored.Score);
        Assert.Equal(game.RemainingCount, restored.RemainingCount);
        Assert.Equal(game.SelectedTileId, restored.SelectedTileId);
        Assert.Equal(game.Status, restored.Status);
        Assert.Equal(game.Tiles.Select(t => (t.Id, t.Coord, t.Face, t.Removed)),
            restored.Tiles.Select(t => (t.Id, t.Coord, t.Face, t.Removed)));
    }
}

internal static class SnapshotExtensions
{
    public static int RemainingCount(this GameSnapshot snapshot) => snapshot.Tiles.Count(t => !t.Removed);
}
