namespace RokMahjong.Core;

public sealed class MahjongGame
{
    private readonly List<Tile> _tiles;
    private readonly Dictionary<int, Tile> _byId;
    private readonly List<HistoryEntry> _history = new();

    public MahjongGame(DealResult deal)
    {
        Layout = deal.Layout;
        Deck = deal.Deck;
        Seed = deal.Seed;
        _tiles = deal.Tiles.ToList();
        _byId = _tiles.ToDictionary(tile => tile.Id);
        Status = GameStatus.Playing;
    }

    private MahjongGame(BoardLayout layout, DeckProfile deck, int seed, IEnumerable<Tile> tiles)
    {
        Layout = layout;
        Deck = deck;
        Seed = seed;
        _tiles = tiles.ToList();
        _byId = _tiles.ToDictionary(tile => tile.Id);
    }

    public BoardLayout Layout { get; }
    public DeckProfile Deck { get; }
    public int Seed { get; }
    public IReadOnlyList<Tile> Tiles => _tiles;
    public GameStatus Status { get; private set; }
    public int Score { get; private set; }
    public int Combo { get; private set; }
    public int Moves { get; private set; }
    public int SelectedTileId { get; private set; } = -1;
    public int LastMatchedFirst { get; private set; } = -1;
    public int LastMatchedSecond { get; private set; } = -1;
    public int RemainingCount => _tiles.Count(tile => !tile.Removed);
    public bool CanUndo => _history.Count > 0;

    public static MahjongGame Create(BoardLayout layout, DeckProfile deck, int seed) =>
        new(DealGenerator.Generate(layout, deck, seed));

    public Tile? GetTile(int id) => _byId.TryGetValue(id, out var tile) ? tile : null;

    public bool IsFree(Tile tile) => FreeRule.IsFree(tile, _tiles);

    public IReadOnlyList<Tile> FreeTiles()
    {
        var result = new List<Tile>();
        foreach (var tile in _tiles)
            if (FreeRule.IsFree(tile, _tiles)) result.Add(tile);
        return result;
    }

    public SelectResult TrySelect(int tileId)
    {
        LastMatchedFirst = -1;
        LastMatchedSecond = -1;
        if (Status != GameStatus.Playing) return SelectResult.Ignored;
        if (!_byId.TryGetValue(tileId, out var tile) || tile.Removed) return SelectResult.Ignored;
        if (!FreeRule.IsFree(tile, _tiles)) return SelectResult.Blocked;

        if (SelectedTileId == tileId)
        {
            SelectedTileId = -1;
            return SelectResult.Deselected;
        }

        if (SelectedTileId < 0)
        {
            SelectedTileId = tileId;
            return SelectResult.Selected;
        }

        var first = _byId[SelectedTileId];
        if (!first.Face.Matches(tile.Face))
        {
            SelectedTileId = tileId;
            Combo = 0;
            return SelectResult.Mismatch;
        }

        var scoreBefore = Score;
        var comboBefore = Combo;
        SelectedTileId = -1;
        first.Removed = true;
        tile.Removed = true;
        LastMatchedFirst = first.Id;
        LastMatchedSecond = tile.Id;
        Combo = Math.Min(5, Combo + 1);
        Score += 100 * Combo;
        Moves++;
        _history.Add(new MatchEntry(first.Id, tile.Id, scoreBefore, comboBefore));
        UpdateStatus();
        return SelectResult.Matched;
    }

    public HintResult Hint()
    {
        var free = FreeTiles()
            .OrderByDescending(tile => tile.Coord.Z)
            .ThenByDescending(tile => tile.Coord.Y)
            .ThenBy(tile => tile.Coord.X)
            .ToList();

        for (var i = 0; i < free.Count; i++)
            for (var j = i + 1; j < free.Count; j++)
                if (free[i].Face.Matches(free[j].Face))
                    return new HintResult(true, free[i].Id, free[j].Id);

        return HintResult.None;
    }

    public ShuffleResult Shuffle(ISeededRandom rng)
    {
        var remaining = _tiles.Where(tile => !tile.Removed).ToList();
        if (remaining.Count < 2) return new ShuffleResult(false, 0);

        var counts = new Dictionary<TileFace, int>();
        foreach (var tile in remaining)
            counts[tile.Face] = counts.TryGetValue(tile.Face, out var count) ? count + 1 : 1;

        var pairUnits = new List<TileFace>();
        foreach (var (face, count) in counts)
        {
            if (count % 2 != 0) return new ShuffleResult(false, 0);
            for (var i = 0; i < count / 2; i++) pairUnits.Add(face);
        }
        rng.Shuffle(pairUnits);

        var occupied = new bool[remaining.Count];
        Array.Fill(occupied, true);
        var assignments = new TileFace?[remaining.Count];
        var solution = new List<(int First, int Second)>();
        var pairIndex = 0;
        var left = remaining.Count;
        while (left > 0)
        {
            var coords = new List<TileCoord>();
            for (var i = 0; i < remaining.Count; i++)
                if (occupied[i]) coords.Add(remaining[i].Coord);

            var free = new List<int>();
            for (var i = 0; i < remaining.Count; i++)
                if (occupied[i] && FreeRule.IsFree(remaining[i].Coord, coords)) free.Add(i);

            if (free.Count < 2) return new ShuffleResult(false, 0);
            var a = free[rng.Next(free.Count)];
            var b = free[rng.Next(free.Count)];
            if (a == b) b = free[(free.IndexOf(a) + 1) % free.Count];
            var face = pairUnits[pairIndex++];
            assignments[a] = face;
            assignments[b] = face;
            solution.Add((remaining[a].Id, remaining[b].Id));
            occupied[a] = false;
            occupied[b] = false;
            left -= 2;
        }

        var oldFaces = remaining.ToDictionary(tile => tile.Id, tile => tile.Face);
        for (var i = 0; i < remaining.Count; i++)
            remaining[i].Face = assignments[i] ?? remaining[i].Face;

        _history.Add(new ShuffleEntry(oldFaces, Score, Combo));
        SelectedTileId = -1;
        Combo = 0;
        UpdateStatus();
        return new ShuffleResult(true, remaining.Count, solution);
    }

    public bool Undo()
    {
        if (_history.Count == 0) return false;
        var entry = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        entry.Undo(this);
        SelectedTileId = -1;
        LastMatchedFirst = -1;
        LastMatchedSecond = -1;
        Status = GameStatus.Playing;
        return true;
    }

    public GameSnapshot Snapshot() => new()
    {
        LayoutId = Layout.Id,
        DeckId = Deck.Id,
        Seed = Seed,
        SelectedTileId = SelectedTileId,
        Score = Score,
        Combo = Combo,
        Moves = Moves,
        Status = Status,
        Tiles = _tiles.Select(tile => new TileSnapshot
        {
            Id = tile.Id,
            X = tile.Coord.X,
            Y = tile.Coord.Y,
            Z = tile.Coord.Z,
            Group = tile.Face.Group,
            Rank = tile.Face.Rank,
            Label = tile.Face.Label,
            Removed = tile.Removed,
        }).ToList(),
        History = _history.Select(entry => entry.ToSnapshot()).ToList(),
    };

    public static MahjongGame FromSnapshot(GameSnapshot snapshot)
    {
        var layout = Layouts.GetLayout(snapshot.LayoutId);
        var deck = Layouts.GetDeck(snapshot.DeckId);
        var tiles = snapshot.Tiles.Select(tile => new Tile(
            tile.Id,
            new TileCoord(tile.X, tile.Y, tile.Z),
            new TileFace(tile.Group, tile.Rank, tile.Label))
        {
            Removed = tile.Removed,
        });
        var game = new MahjongGame(layout, deck, snapshot.Seed, tiles)
        {
            SelectedTileId = snapshot.SelectedTileId,
            Score = snapshot.Score,
            Combo = snapshot.Combo,
            Moves = snapshot.Moves,
            Status = snapshot.Status,
        };
        foreach (var history in snapshot.History)
            game._history.Add(HistoryEntry.FromSnapshot(history));
        if (game.Status == GameStatus.Playing) game.UpdateStatus();
        return game;
    }

    private void UpdateStatus()
    {
        if (_tiles.All(tile => tile.Removed))
        {
            Status = GameStatus.Won;
            return;
        }

        var freeByKey = new Dictionary<string, int>();
        foreach (var tile in _tiles)
        {
            if (tile.Removed || !FreeRule.IsFree(tile, _tiles)) continue;
            var key = tile.Face.MatchKey;
            freeByKey[key] = freeByKey.TryGetValue(key, out var count) ? count + 1 : 1;
            if (freeByKey[key] >= 2)
            {
                Status = GameStatus.Playing;
                return;
            }
        }
        Status = GameStatus.Deadlocked;
    }

    private abstract class HistoryEntry
    {
        protected HistoryEntry(int scoreBefore, int comboBefore)
        {
            ScoreBefore = scoreBefore;
            ComboBefore = comboBefore;
        }

        protected int ScoreBefore { get; }
        protected int ComboBefore { get; }
        public abstract void Undo(MahjongGame game);
        public abstract HistorySnapshot ToSnapshot();

        public static HistoryEntry FromSnapshot(HistorySnapshot snapshot) => snapshot.Kind switch
        {
            "match" => new MatchEntry(snapshot.First, snapshot.Second, snapshot.ScoreBefore, snapshot.ComboBefore),
            "shuffle" => new ShuffleEntry(
                snapshot.Faces!.ToDictionary(change => change.TileId, change => new TileFace(change.Group, change.Rank, change.Label)),
                snapshot.ScoreBefore,
                snapshot.ComboBefore),
            _ => throw new InvalidOperationException($"Unknown history kind '{snapshot.Kind}'."),
        };
    }

    private sealed class MatchEntry : HistoryEntry
    {
        private readonly int _first;
        private readonly int _second;

        public MatchEntry(int first, int second, int scoreBefore, int comboBefore) : base(scoreBefore, comboBefore)
        {
            _first = first;
            _second = second;
        }

        public override void Undo(MahjongGame game)
        {
            game._byId[_first].Removed = false;
            game._byId[_second].Removed = false;
            game.Score = ScoreBefore;
            game.Combo = ComboBefore;
            game.Moves = Math.Max(0, game.Moves - 1);
        }

        public override HistorySnapshot ToSnapshot() => new()
        {
            Kind = "match",
            First = _first,
            Second = _second,
            ScoreBefore = ScoreBefore,
            ComboBefore = ComboBefore,
        };
    }

    private sealed class ShuffleEntry : HistoryEntry
    {
        private readonly Dictionary<int, TileFace> _oldFaces;

        public ShuffleEntry(Dictionary<int, TileFace> oldFaces, int scoreBefore, int comboBefore) : base(scoreBefore, comboBefore)
        {
            _oldFaces = oldFaces;
        }

        public override void Undo(MahjongGame game)
        {
            foreach (var (tileId, face) in _oldFaces)
                if (game._byId.TryGetValue(tileId, out var tile)) tile.Face = face;
            game.Score = ScoreBefore;
            game.Combo = ComboBefore;
        }

        public override HistorySnapshot ToSnapshot() => new()
        {
            Kind = "shuffle",
            ScoreBefore = ScoreBefore,
            ComboBefore = ComboBefore,
            Faces = _oldFaces.Select(pair => new FaceChange
            {
                TileId = pair.Key,
                Group = pair.Value.Group,
                Rank = pair.Value.Rank,
                Label = pair.Value.Label,
            }).ToList(),
        };
    }
}

public sealed class GameSnapshot
{
    public string LayoutId { get; set; } = "";
    public string DeckId { get; set; } = "";
    public int Seed { get; set; }
    public int SelectedTileId { get; set; } = -1;
    public int Score { get; set; }
    public int Combo { get; set; }
    public int Moves { get; set; }
    public GameStatus Status { get; set; }
    public List<TileSnapshot> Tiles { get; set; } = new();
    public List<HistorySnapshot> History { get; set; } = new();
}

public sealed class TileSnapshot
{
    public int Id { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public MatchGroup Group { get; set; }
    public int Rank { get; set; }
    public string Label { get; set; } = "";
    public bool Removed { get; set; }
}

public sealed class HistorySnapshot
{
    public string Kind { get; set; } = "";
    public int First { get; set; }
    public int Second { get; set; }
    public int ScoreBefore { get; set; }
    public int ComboBefore { get; set; }
    public List<FaceChange>? Faces { get; set; }
}

public sealed class FaceChange
{
    public int TileId { get; set; }
    public MatchGroup Group { get; set; }
    public int Rank { get; set; }
    public string Label { get; set; } = "";
}
