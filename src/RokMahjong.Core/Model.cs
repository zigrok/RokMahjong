namespace RokMahjong.Core;

public enum MatchGroup
{
    Characters,
    Bamboo,
    Circles,
    Wind,
    Dragon,
    Flower,
    Season,
}

public readonly record struct TileFace(MatchGroup Group, int Rank, string Label)
{
    public bool IsBonus => Group is MatchGroup.Flower or MatchGroup.Season;

    public bool Matches(TileFace other) =>
        IsBonus ? Group == other.Group : Group == other.Group && Rank == other.Rank;

    public string MatchKey => IsBonus ? Group.ToString() : $"{Group}:{Rank}";

    public override string ToString() => Label;
}

public readonly record struct TileCoord(int X, int Y, int Z)
{
    public bool Overlaps(TileCoord other) =>
        Math.Abs(X - other.X) < 2 && Math.Abs(Y - other.Y) < 2;

    public bool IsLeftOf(TileCoord other) => X + 2 == other.X;
    public bool IsRightOf(TileCoord other) => X == other.X + 2;
}

public sealed class Tile
{
    public Tile(int id, TileCoord coord, TileFace face)
    {
        Id = id;
        Coord = coord;
        Face = face;
    }

    public int Id { get; }
    public TileCoord Coord { get; }
    public TileFace Face { get; set; }
    public bool Removed { get; set; }
}

public sealed record BoardLayout(
    string Id,
    string Family,
    string Name,
    IReadOnlyList<TileCoord> Slots,
    bool PortraitFriendly)
{
    public int TileCount => Slots.Count;

    public float WorldColumns => Slots.Count == 0 ? 1 : (Slots.Max(s => s.X) + 2) / 2f;
    public float WorldRows => Slots.Count == 0 ? 1 : (Slots.Max(s => s.Y) + 2) / 2f;
}

public sealed record DeckEntry(TileFace Face, int Copies);

public sealed record DeckProfile(string Id, IReadOnlyList<DeckEntry> Entries)
{
    public int TileCount => Entries.Sum(entry => entry.Copies);
    public int PairCount => TileCount / 2;
    public IEnumerable<TileFace> AllFaces => Entries.SelectMany(entry => Enumerable.Repeat(entry.Face, entry.Copies));
}

public sealed record LevelDefinition(
    int Index,
    string Name,
    string LayoutId,
    string DeckId,
    int Seed,
    string Description);

public enum GameStatus
{
    Playing,
    Won,
    Deadlocked,
}

public enum SelectResult
{
    Selected,
    Deselected,
    Matched,
    Mismatch,
    Blocked,
    Ignored,
}

public readonly record struct HintResult(bool HasHint, int FirstTileId, int SecondTileId)
{
    public static HintResult None => new(false, -1, -1);
}

public readonly record struct ShuffleResult(
    bool Changed,
    int MovedTiles,
    IReadOnlyList<(int First, int Second)>? Solution = null);
