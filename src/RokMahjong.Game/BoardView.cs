using Forma;
using Microsoft.Xna.Framework;
using RokMahjong.Core;

namespace RokMahjong.Game;

public sealed class MahjongBoardView : DrawingElement
{
    private static readonly DrawingPath UnitRect = new DrawingPath()
        .MoveTo(Vector2.Zero)
        .LineTo(Vector2.UnitX)
        .LineTo(Vector2.One)
        .LineTo(Vector2.UnitY)
        .Close();

    private static readonly Color Felt = new(22, 72, 60);
    private static readonly Color FeltEdge = new(44, 120, 96);
    private static readonly Color Shadow = new(0, 0, 0, 90);
    private static readonly Color Side = new(176, 154, 112);
    private static readonly Color Face = new(250, 241, 218);
    private static readonly Color FaceBlocked = new(206, 199, 181);
    private static readonly Color FaceSelected = new(255, 226, 157);
    private static readonly Color Border = new(196, 163, 91);
    private static readonly Color FocusColor = new(71, 228, 255);
    private static readonly Color HintColor = new(255, 196, 76);

    private BoardController? _controller;

    public MahjongBoardView(UIFont tileFont)
    {
        TileFont = tileFont;
        IsHitTestVisible = false;
        MouseFilter = MouseFilter.Ignore;
    }

    public UIFont TileFont { get; set; }
    public TimeSpan Now { get; set; }
    public bool HighlightFreeTiles { get; set; } = true;
    public bool ReducedMotion { get; set; }

    public void SetBoard(BoardController? controller)
    {
        _controller = controller;
        QueueLayout();
    }

    protected override void Draw(DrawingContext context)
    {
        if (_controller is null) return;
        var presentation = _controller.Presentation;
        var panel = presentation.PanelRect;
        Fill(context, panel, Felt);
        StrokeRect(context, panel, FeltEdge, 2f);

        var selected = _controller.Game.SelectedTileId;
        var focus = _controller.FocusTileId;
        var hintPulse = ReducedMotion ? 1f : (float)(0.5 + 0.5 * Math.Sin(Now.TotalSeconds * 5.0));

        foreach (var tile in presentation.SortForDraw(_controller.Game.Tiles))
        {
            var rect = presentation.GetFaceRect(tile);
            var free = _controller.Game.IsFree(tile);
            var isSelected = tile.Id == selected;
            var isFocused = tile.Id == focus;
            var isHint = tile.Id == _controller.HintFirstTileId || tile.Id == _controller.HintSecondTileId;
            if (isSelected && !ReducedMotion) rect.Offset(-3, -6);

            Fill(context, new Rectangle(rect.X + 4, rect.Y + 6, rect.Width, rect.Height), Shadow);
            Fill(context, new Rectangle(rect.X + 3, rect.Y + 4, rect.Width, rect.Height), Side);

            var faceColor = isSelected ? FaceSelected : free ? Face : FaceBlocked;
            if (HighlightFreeTiles && !free) faceColor = Color.Lerp(faceColor, new Color(180, 186, 178), 0.35f);
            Fill(context, rect, faceColor);
            StrokeRect(context, rect, Border, 1.5f);

            if (isSelected || isFocused || isHint)
            {
                var color = isHint ? HintColor * (0.55f + hintPulse * 0.45f) : isSelected ? FaceSelected : FocusColor;
                StrokeRect(context, rect, color, isHint ? 3f : 2f);
            }

            var label = tile.Face.Label;
            var size = context.MeasureText(TileFont, label);
            var textPosition = new Vector2(
                rect.X + (rect.Width - size.X) / 2f,
                rect.Y + (rect.Height - size.Y) / 2f - 1);
            context.DrawText(TileFont, label, textPosition, FaceColor(tile.Face));
        }
    }

    private static Color FaceColor(TileFace face) => face.Group switch
    {
        MatchGroup.Characters => new Color(164, 42, 45),
        MatchGroup.Bamboo => new Color(31, 108, 68),
        MatchGroup.Circles => new Color(39, 77, 138),
        MatchGroup.Wind => new Color(52, 58, 70),
        MatchGroup.Dragon => face.Rank switch
        {
            1 => new Color(178, 42, 42),
            2 => new Color(31, 120, 66),
            _ => new Color(54, 76, 120),
        },
        MatchGroup.Flower => new Color(156, 60, 128),
        MatchGroup.Season => new Color(176, 106, 32),
        _ => new Color(52, 58, 70),
    };

    private static void Fill(DrawingContext context, Rectangle rect, Color color)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        var transform = Matrix.CreateScale(rect.Width, rect.Height, 1) *
                        Matrix.CreateTranslation(rect.X, rect.Y, 0);
        context.FillPath(UnitRect, color, transform);
    }

    private static void StrokeRect(DrawingContext context, Rectangle rect, Color color, float thickness)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        var transform = Matrix.CreateScale(rect.Width, rect.Height, 1) *
                        Matrix.CreateTranslation(rect.X, rect.Y, 0);
        context.StrokePath(UnitRect, new SolidColorBrush(color), rect, thickness, transform);
    }
}
