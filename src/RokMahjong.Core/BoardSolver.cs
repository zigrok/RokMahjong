namespace RokMahjong.Core;

public enum SolverOutcome
{
    Solved,
    Deadlocked,
    Inconclusive,
}

public readonly record struct SolverResult(SolverOutcome Outcome, int Nodes);

public static class BoardSolver
{
    public static SolverResult Solve(MahjongGame game, int maxNodes = 250_000)
    {
        var tiles = game.Tiles
            .Select(tile => new Tile(tile.Id, tile.Coord, tile.Face) { Removed = tile.Removed })
            .ToList();
        var remaining = tiles.Count(tile => !tile.Removed);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var nodes = 0;
        var exhausted = false;

        bool Search(int left)
        {
            if (left == 0) return true;
            if (nodes++ >= maxNodes)
            {
                exhausted = true;
                return false;
            }

            var key = string.Join(",", tiles.Where(tile => !tile.Removed).Select(tile => tile.Id).OrderBy(id => id));
            if (!visited.Add(key)) return false;

            var free = tiles.Where(tile => !tile.Removed && FreeRule.IsFree(tile, tiles)).ToList();
            for (var i = 0; i < free.Count; i++)
                for (var j = i + 1; j < free.Count; j++)
                {
                    if (!free[i].Face.Matches(free[j].Face)) continue;
                    free[i].Removed = true;
                    free[j].Removed = true;
                    if (Search(left - 2)) return true;
                    free[i].Removed = false;
                    free[j].Removed = false;
                    if (exhausted) return false;
                }

            return false;
        }

        if (Search(remaining)) return new SolverResult(SolverOutcome.Solved, nodes);
        return new SolverResult(exhausted ? SolverOutcome.Inconclusive : SolverOutcome.Deadlocked, nodes);
    }
}
