namespace RokMahjong.Core;

public sealed record DealResult(
    BoardLayout Layout,
    DeckProfile Deck,
    int Seed,
    IReadOnlyList<Tile> Tiles,
    IReadOnlyList<(int First, int Second)> Solution);

public static class DealGenerator
{
    public static DealResult Generate(BoardLayout layout, DeckProfile deck, int seed)
    {
        if (layout.TileCount != deck.TileCount)
            throw new InvalidOperationException(
                $"Layout {layout.Id} has {layout.TileCount} slots but deck {deck.Id} has {deck.TileCount} tiles.");

        for (var attempt = 0; attempt < 128; attempt++)
        {
            var rng = new SeededRandom(unchecked(seed + attempt * 7919));
            var allocated = new TileFace?[layout.TileCount];
            var occupied = new bool[layout.TileCount];
            for (var i = 0; i < occupied.Length; i++) occupied[i] = true;

            var pairs = BuildPairUnits(deck);
            rng.Shuffle(pairs);
            var pairIndex = 0;
            var solution = new List<(int First, int Second)>();
            var remaining = layout.TileCount;

            while (remaining > 0)
            {
                var free = new List<int>();
                var occupiedCoords = new List<TileCoord>();
                for (var i = 0; i < layout.TileCount; i++)
                    if (occupied[i]) occupiedCoords.Add(layout.Slots[i]);

                for (var i = 0; i < layout.TileCount; i++)
                {
                    if (!occupied[i]) continue;
                    if (FreeRule.IsFree(layout.Slots[i], occupiedCoords)) free.Add(i);
                }

                if (free.Count < 2) break;

                var a = free[rng.Next(free.Count)];
                var b = free[rng.Next(free.Count)];
                if (a == b)
                {
                    b = free[(free.IndexOf(a) + 1) % free.Count];
                }

                var face = pairs[pairIndex++];
                allocated[a] = face;
                allocated[b] = face;
                occupied[a] = false;
                occupied[b] = false;
                solution.Add((a, b));
                remaining -= 2;
            }

            if (remaining != 0) continue;

            var tiles = new List<Tile>(layout.TileCount);
            for (var i = 0; i < layout.TileCount; i++)
            {
                var face = allocated[i] ?? throw new InvalidOperationException("Dealer left a slot unassigned.");
                tiles.Add(new Tile(i, layout.Slots[i], face));
            }

            if (!Verify(tiles, solution)) continue;
            return new DealResult(layout, deck, seed, tiles, solution);
        }

        throw new InvalidOperationException($"Could not generate a solvable deal for {layout.Id} / {deck.Id} seed {seed}.");
    }

    public static bool Verify(IReadOnlyList<Tile> tiles, IReadOnlyList<(int First, int Second)> solution)
    {
        var alive = tiles.Select(tile => new Tile(tile.Id, tile.Coord, tile.Face)).ToList();
        foreach (var (firstId, secondId) in solution)
        {
            var first = alive.FirstOrDefault(tile => tile.Id == firstId && !tile.Removed);
            var second = alive.FirstOrDefault(tile => tile.Id == secondId && !tile.Removed);
            if (first is null || second is null) return false;
            if (!FreeRule.IsFree(first, alive) || !FreeRule.IsFree(second, alive)) return false;
            if (!first.Face.Matches(second.Face)) return false;
            first.Removed = true;
            second.Removed = true;
        }
        return alive.All(tile => tile.Removed);
    }

    private static List<TileFace> BuildPairUnits(DeckProfile deck)
    {
        var pairs = new List<TileFace>(deck.PairCount);
        foreach (var face in deck.Faces)
            for (var i = 0; i < deck.CopiesPerFace / 2; i++)
                pairs.Add(face);
        return pairs;
    }
}
