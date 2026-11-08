namespace RokMahjong.Core;

public static class Layouts
{
    public static BoardLayout Lotus36 { get; } = BuildLotus36();
    public static BoardLayout Lotus72 { get; } = BuildLotus72();
    public static BoardLayout Pagoda72 { get; } = BuildPagoda72();
    public static BoardLayout Pagoda108 { get; } = BuildPagoda108();
    public static BoardLayout Pagoda144 { get; } = BuildPagoda144();
    public static BoardLayout Turtle144 { get; } = BuildTurtle144();

    public static IReadOnlyList<BoardLayout> All { get; } = new[]
    {
        Lotus36, Lotus72, Pagoda72, Pagoda108, Pagoda144, Turtle144,
    };

    public static IReadOnlyList<TileFace> Faces { get; } = BuildFaces();

    public static DeckProfile Mini36 { get; } = BuildDeck("mini36", Faces.Take(18), 2);
    public static DeckProfile Classic72 { get; } = BuildDeck("classic72", Faces.Take(18), 4);
    public static DeckProfile Classic108 { get; } = BuildDeck("classic108", Faces.Take(27), 4);
    public static DeckProfile Classic144 { get; } = new("classic144",
        Faces.Take(34).Select(face => new DeckEntry(face, 4))
            .Concat(Faces.Skip(34).Take(4).Select(face => new DeckEntry(face, 1)))
            .Concat(Faces.Skip(38).Take(4).Select(face => new DeckEntry(face, 1)))
            .ToArray());

    public static IReadOnlyList<DeckProfile> Decks { get; } = new[]
    {
        Mini36, Classic72, Classic108, Classic144,
    };

    public static IReadOnlyList<LevelDefinition> Levels { get; } = new[]
    {
        new LevelDefinition(1, "First Bloom", Lotus36.Id, Mini36.Id, 1101, "A gentle six-by-six opening."),
        new LevelDefinition(2, "Lotus Morning", Lotus36.Id, Mini36.Id, 1102, "Another compact lotus."),
        new LevelDefinition(3, "Lotus Path", Lotus72.Id, Classic72.Id, 2201, "The first layered lotus."),
        new LevelDefinition(4, "Lotus Shade", Lotus72.Id, Classic72.Id, 2202, "A wider lotus with two layers."),
        new LevelDefinition(5, "Pagoda Steps", Pagoda72.Id, Classic72.Id, 3301, "Climb the first pagoda."),
        new LevelDefinition(6, "Pagoda Lanterns", Pagoda72.Id, Classic72.Id, 3302, "A taller pagoda stack."),
        new LevelDefinition(7, "Pagoda Garden", Pagoda108.Id, Classic108.Id, 4401, "Three suits, three layers."),
        new LevelDefinition(8, "Pagoda Rain", Pagoda108.Id, Classic108.Id, 4402, "A deeper pagoda."),
        new LevelDefinition(9, "Jade Pavilion", Pagoda144.Id, Classic144.Id, 5501, "The full pagoda, all suits."),
        new LevelDefinition(10, "Moonlit Pagoda", Pagoda144.Id, Classic144.Id, 5502, "The full pagoda again."),
        new LevelDefinition(11, "Turtle Bay", Turtle144.Id, Classic144.Id, 6601, "The classic wide turtle."),
        new LevelDefinition(12, "Turtle Dawn", Turtle144.Id, Classic144.Id, 6602, "The final wide board."),
    };

    public static BoardLayout GetLayout(string id) =>
        All.FirstOrDefault(layout => layout.Id == id)
        ?? throw new ArgumentException($"Unknown layout '{id}'.", nameof(id));

    public static DeckProfile GetDeck(string id) =>
        Decks.FirstOrDefault(deck => deck.Id == id)
        ?? throw new ArgumentException($"Unknown deck '{id}'.", nameof(id));

    private static DeckProfile BuildDeck(string id, IEnumerable<TileFace> faces, int copies) =>
        new(id, faces.Select(face => new DeckEntry(face, copies)).ToArray());

    public static LevelDefinition GetLevel(int oneBasedIndex) =>
        Levels.FirstOrDefault(level => level.Index == oneBasedIndex)
        ?? throw new ArgumentOutOfRangeException(nameof(oneBasedIndex));

    private static IReadOnlyList<TileFace> BuildFaces()
    {
        var faces = new List<TileFace>(36);
        for (var rank = 1; rank <= 9; rank++) faces.Add(new TileFace(MatchGroup.Characters, rank, $"C{rank}"));
        for (var rank = 1; rank <= 9; rank++) faces.Add(new TileFace(MatchGroup.Bamboo, rank, $"B{rank}"));
        for (var rank = 1; rank <= 9; rank++) faces.Add(new TileFace(MatchGroup.Circles, rank, $"D{rank}"));
        var winds = new[] { "E", "S", "W", "N" };
        for (var rank = 0; rank < winds.Length; rank++) faces.Add(new TileFace(MatchGroup.Wind, rank + 1, winds[rank]));
        var dragons = new[] { "R", "G", "Wh" };
        for (var rank = 0; rank < dragons.Length; rank++) faces.Add(new TileFace(MatchGroup.Dragon, rank + 1, dragons[rank]));
        var flowers = new[] { "梅", "蘭", "菊", "竹" };
        for (var rank = 0; rank < flowers.Length; rank++) faces.Add(new TileFace(MatchGroup.Flower, rank + 1, $"F{rank + 1}"));
        var seasons = new[] { "春", "夏", "秋", "冬" };
        for (var rank = 0; rank < seasons.Length; rank++) faces.Add(new TileFace(MatchGroup.Season, rank + 1, $"Se{rank + 1}"));
        return faces;
    }

    private static BoardLayout BuildLotus36()
    {
        var slots = new List<TileCoord>();
        AddGrid(slots, columns: 6, rows: 6, z: 0);
        return new BoardLayout("lotus36", "Lotus", "Lotus 36", slots, PortraitFriendly: true);
    }

    private static BoardLayout BuildLotus72()
    {
        var slots = new List<TileCoord>();
        AddGrid(slots, columns: 8, rows: 6, z: 0);
        AddGrid(slots, columns: 6, rows: 4, z: 1, startX: 2, startY: 2);
        return new BoardLayout("lotus72", "Lotus", "Lotus 72", slots, PortraitFriendly: true);
    }

    private static BoardLayout BuildPagoda72()
    {
        var slots = new List<TileCoord>();
        AddGrid(slots, columns: 6, rows: 8, z: 0);
        AddGrid(slots, columns: 4, rows: 4, z: 1, startX: 2, startY: 4);
        AddGrid(slots, columns: 2, rows: 4, z: 2, startX: 4, startY: 4);
        return new BoardLayout("pagoda72", "Pagoda", "Pagoda 72", slots, PortraitFriendly: true);
    }

    private static BoardLayout BuildPagoda108()
    {
        var slots = new List<TileCoord>();
        AddGrid(slots, columns: 8, rows: 9, z: 0);
        AddGrid(slots, columns: 6, rows: 6, z: 1, startX: 2, startY: 3);
        return new BoardLayout("pagoda108", "Pagoda", "Pagoda 108", slots, PortraitFriendly: true);
    }

    private static BoardLayout BuildPagoda144()
    {
        var slots = new List<TileCoord>();
        AddGrid(slots, columns: 8, rows: 9, z: 0);
        AddGrid(slots, columns: 6, rows: 7, z: 1, startX: 2, startY: 2);
        AddGrid(slots, columns: 4, rows: 5, z: 2, startX: 4, startY: 4);
        AddGrid(slots, columns: 2, rows: 3, z: 3, startX: 6, startY: 6);
        AddGrid(slots, columns: 1, rows: 4, z: 4, startX: 7, startY: 6);
        return new BoardLayout("pagoda144", "Pagoda", "Pagoda 144", slots, PortraitFriendly: true);
    }

    private static BoardLayout BuildTurtle144()
    {
        var slots = new List<TileCoord>();
        AddGrid(slots, columns: 12, rows: 8, z: 0);
        AddGrid(slots, columns: 8, rows: 4, z: 1, startX: 4, startY: 3);
        AddGrid(slots, columns: 5, rows: 2, z: 2, startX: 5, startY: 5);
        AddGrid(slots, columns: 2, rows: 2, z: 3, startX: 7, startY: 5);
        AddGrid(slots, columns: 1, rows: 2, z: 4, startX: 8, startY: 6);
        return new BoardLayout("turtle144", "Turtle", "Turtle 144", slots, PortraitFriendly: false);
    }

    private static void AddGrid(
        List<TileCoord> slots,
        int columns,
        int rows,
        int z,
        int startX = 0,
        int startY = 0)
    {
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
                slots.Add(new TileCoord(startX + column * 2, startY + row * 2, z));
    }
}
