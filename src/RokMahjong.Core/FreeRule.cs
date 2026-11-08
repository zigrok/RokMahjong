namespace RokMahjong.Core;

public static class FreeRule
{
    public static bool IsFree(Tile tile, IReadOnlyList<Tile> tiles)
    {
        if (tile.Removed) return false;
        foreach (var other in tiles)
        {
            if (other.Removed || ReferenceEquals(other, tile)) continue;
            if (other.Coord.Z > tile.Coord.Z && tile.Coord.Overlaps(other.Coord)) return false;
        }

        var leftBlocked = false;
        var rightBlocked = false;
        foreach (var other in tiles)
        {
            if (other.Removed || ReferenceEquals(other, tile) || other.Coord.Z != tile.Coord.Z) continue;
            if (Math.Abs(other.Coord.Y - tile.Coord.Y) >= 2) continue;
            if (other.Coord.X == tile.Coord.X - 2) leftBlocked = true;
            if (other.Coord.X == tile.Coord.X + 2) rightBlocked = true;
        }
        return !leftBlocked || !rightBlocked;
    }

    public static bool IsFree(TileCoord coord, IReadOnlyList<TileCoord> occupied)
    {
        foreach (var other in occupied)
        {
            if (other.Z > coord.Z && coord.Overlaps(other)) return false;
        }

        var leftBlocked = false;
        var rightBlocked = false;
        foreach (var other in occupied)
        {
            if (other.Z != coord.Z || Math.Abs(other.Y - coord.Y) >= 2) continue;
            if (other.X == coord.X - 2) leftBlocked = true;
            if (other.X == coord.X + 2) rightBlocked = true;
        }
        return !leftBlocked || !rightBlocked;
    }
}
