namespace RokMahjong.Core;

public sealed class GameSession
{
    public GameSession(LevelDefinition level, MahjongGame game, DateTimeOffset? startedAt = null)
    {
        Level = level;
        Game = game;
        StartedAt = startedAt ?? DateTimeOffset.UtcNow;
    }

    public LevelDefinition Level { get; }
    public MahjongGame Game { get; }
    public DateTimeOffset StartedAt { get; }
    public bool Completed => Game.Status == GameStatus.Won;

    public static GameSession Start(int levelIndex)
    {
        var level = Layouts.GetLevel(levelIndex);
        var layout = Layouts.GetLayout(level.LayoutId);
        var deck = Layouts.GetDeck(level.DeckId);
        return new GameSession(level, MahjongGame.Create(layout, deck, level.Seed));
    }

    public static GameSession StartWithSeed(int levelIndex, int seed)
    {
        var level = Layouts.GetLevel(levelIndex);
        var layout = Layouts.GetLayout(level.LayoutId);
        var deck = Layouts.GetDeck(level.DeckId);
        return new GameSession(level, MahjongGame.Create(layout, deck, seed));
    }

    public SessionSnapshot Snapshot() => new()
    {
        LevelIndex = Level.Index,
        StartedAt = StartedAt,
        Game = Game.Snapshot(),
    };

    public static GameSession FromSnapshot(SessionSnapshot snapshot)
    {
        var level = Layouts.GetLevel(snapshot.LevelIndex);
        return new GameSession(level, MahjongGame.FromSnapshot(snapshot.Game), snapshot.StartedAt);
    }
}

public sealed class SessionSnapshot
{
    public int LevelIndex { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public GameSnapshot Game { get; set; } = new();
}
