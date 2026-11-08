using Microsoft.Xna.Framework;
using RokMahjong.Core;

namespace RokMahjong.Game;

public sealed class BoardPresentation
{
    private const float TileAspect = 0.72f;

    public BoardPresentation(BoardLayout layout, Rectangle available)
    {
        Layout = layout;
        AvailableRect = available;
        var maxZ = layout.Slots.Count == 0 ? 0 : layout.Slots.Max(slot => slot.Z);
        MaxZ = maxZ;
        var columns = Math.Max(1, layout.WorldColumns);
        var rows = Math.Max(1, layout.WorldRows);
        var unitWidth = columns + maxZ * 0.12f + 0.15f;
        var unitHeight = rows / TileAspect + maxZ * 0.12f + 0.15f;
        var tileWidth = MathF.Min(available.Width / unitWidth, available.Height / unitHeight);
        TileWidth = Math.Clamp(tileWidth, 14f, 124f);
        TileHeight = TileWidth / TileAspect;
        LayerOffset = TileWidth * 0.12f;

        var panelWidth = columns * TileWidth + maxZ * LayerOffset + TileWidth * 0.15f;
        var panelHeight = rows * TileHeight + maxZ * LayerOffset + TileWidth * 0.15f;
        PanelRect = new Rectangle(
            (int)MathF.Floor(available.X + (available.Width - panelWidth) / 2f),
            (int)MathF.Floor(available.Y + (available.Height - panelHeight) / 2f),
            (int)MathF.Ceiling(panelWidth),
            (int)MathF.Ceiling(panelHeight));
        OriginX = PanelRect.X + maxZ * LayerOffset;
        OriginY = PanelRect.Y + maxZ * LayerOffset;
    }

    public BoardLayout Layout { get; }
    public Rectangle AvailableRect { get; }
    public Rectangle PanelRect { get; }
    public int MaxZ { get; }
    public float TileWidth { get; }
    public float TileHeight { get; }
    public float LayerOffset { get; }
    public float OriginX { get; }
    public float OriginY { get; }

    public Rectangle GetFaceRect(TileCoord coord) => new(
        (int)MathF.Round(OriginX + coord.X / 2f * TileWidth - coord.Z * LayerOffset),
        (int)MathF.Round(OriginY + coord.Y / 2f * TileHeight - coord.Z * LayerOffset),
        Math.Max(1, (int)MathF.Round(TileWidth)),
        Math.Max(1, (int)MathF.Round(TileHeight)));

    public Rectangle GetFaceRect(Tile tile) => GetFaceRect(tile.Coord);

    public Point GetCenter(Tile tile)
    {
        var rect = GetFaceRect(tile);
        return new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }

    public Tile? HitTest(IReadOnlyList<Tile> tiles, Point point)
    {
        foreach (var tile in tiles
                     .Where(tile => !tile.Removed)
                     .OrderByDescending(tile => tile.Coord.Z)
                     .ThenByDescending(tile => tile.Coord.Y)
                     .ThenBy(tile => tile.Coord.X))
            if (GetFaceRect(tile).Contains(point)) return tile;
        return null;
    }

    public IReadOnlyList<Tile> SortForDraw(IReadOnlyList<Tile> tiles) => tiles
        .Where(tile => !tile.Removed)
        .OrderBy(tile => tile.Coord.Z)
        .ThenBy(tile => tile.Coord.Y)
        .ThenBy(tile => tile.Coord.X)
        .ToList();
}
