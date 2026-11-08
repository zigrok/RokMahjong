using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RokMahjong.Core;

namespace RokMahjong.Game;

public sealed class BoardController
{
    private bool _pointerDown;
    private Point _pressPoint;
    private int _dragSourceId = -1;
    private bool _dragMoved;

    public BoardController(MahjongGame game, BoardPresentation presentation)
    {
        Game = game;
        Presentation = presentation;
        FocusTileId = game.FreeTiles().FirstOrDefault()?.Id ?? -1;
    }

    public MahjongGame Game { get; }
    public BoardPresentation Presentation { get; private set; }
    public int FocusTileId { get; set; }
    public int HoverTileId { get; private set; } = -1;
    public int HintFirstTileId { get; private set; } = -1;
    public int HintSecondTileId { get; private set; } = -1;
    public SelectResult LastResult { get; private set; } = SelectResult.Ignored;
    public Rectangle BoardRect => Presentation.AvailableRect;

    public bool Contains(Point point) => BoardRect.Contains(point);

    public void SetPresentation(BoardPresentation presentation) => Presentation = presentation;

    public void PointerPressed(Point point)
    {
        _pointerDown = true;
        _pressPoint = point;
        _dragMoved = false;
        _dragSourceId = -1;
        var tile = Presentation.HitTest(Game.Tiles, point);
        if (tile is null)
        {
            if (Game.SelectedTileId >= 0) Game.TrySelect(Game.SelectedTileId);
            return;
        }

        FocusTileId = tile.Id;
        LastResult = Game.TrySelect(tile.Id);
        if (LastResult == SelectResult.Selected)
        {
            _dragSourceId = tile.Id;
            ClearHint();
        }
    }

    public void PointerMoved(Point point)
    {
        HoverTileId = Presentation.HitTest(Game.Tiles, point)?.Id ?? -1;
        if (!_pointerDown || _dragSourceId < 0) return;
        var dx = point.X - _pressPoint.X;
        var dy = point.Y - _pressPoint.Y;
        if (dx * dx + dy * dy >= 8 * 8) _dragMoved = true;
    }

    public void PointerReleased(Point point)
    {
        if (_dragMoved && _dragSourceId >= 0)
        {
            var target = Presentation.HitTest(Game.Tiles, point);
            if (target is not null && target.Id != _dragSourceId)
                LastResult = Game.TrySelect(target.Id);
        }

        _pointerDown = false;
        _dragSourceId = -1;
        _dragMoved = false;
    }

    public void CancelSelection()
    {
        if (Game.SelectedTileId >= 0) Game.TrySelect(Game.SelectedTileId);
        ClearHint();
    }

    public void MoveFocus(Keys key)
    {
        var free = Game.FreeTiles();
        if (free.Count == 0) return;
        var current = free.FirstOrDefault(tile => tile.Id == FocusTileId) ?? free[0];
        var origin = Presentation.GetCenter(current);
        Tile? best = null;
        var bestScore = float.MaxValue;
        foreach (var candidate in free)
        {
            if (candidate.Id == current.Id) continue;
            var center = Presentation.GetCenter(candidate);
            var dx = center.X - origin.X;
            var dy = center.Y - origin.Y;
            float primary, secondary;
            switch (key)
            {
                case Keys.Left when dx < 0: primary = -dx; secondary = MathF.Abs(dy); break;
                case Keys.Right when dx > 0: primary = dx; secondary = MathF.Abs(dy); break;
                case Keys.Up when dy < 0: primary = -dy; secondary = MathF.Abs(dx); break;
                case Keys.Down when dy > 0: primary = dy; secondary = MathF.Abs(dx); break;
                default: continue;
            }
            var score = primary + secondary * 0.6f;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }
        if (best is not null) FocusTileId = best.Id;
    }

    public void CycleFocus()
    {
        var free = Game.FreeTiles().OrderBy(tile => tile.Coord.Z)
            .ThenBy(tile => tile.Coord.Y)
            .ThenBy(tile => tile.Coord.X)
            .ThenBy(tile => tile.Id)
            .ToList();
        if (free.Count == 0)
        {
            FocusTileId = -1;
            return;
        }
        var index = free.FindIndex(tile => tile.Id == FocusTileId);
        FocusTileId = free[(index + 1 + free.Count) % free.Count].Id;
    }

    public void ActivateFocus()
    {
        if (FocusTileId < 0) return;
        LastResult = Game.TrySelect(FocusTileId);
        if (LastResult == SelectResult.Matched)
        {
            FocusTileId = Game.FreeTiles().FirstOrDefault()?.Id ?? -1;
            ClearHint();
        }
    }

    public HintResult Hint()
    {
        var hint = Game.Hint();
        HintFirstTileId = hint.FirstTileId;
        HintSecondTileId = hint.SecondTileId;
        if (hint.HasHint) FocusTileId = hint.FirstTileId;
        return hint;
    }

    public ShuffleResult Shuffle(ISeededRandom rng)
    {
        var result = Game.Shuffle(rng);
        FocusTileId = Game.FreeTiles().FirstOrDefault()?.Id ?? -1;
        ClearHint();
        return result;
    }

    public bool Undo()
    {
        var changed = Game.Undo();
        FocusTileId = Game.FreeTiles().FirstOrDefault()?.Id ?? -1;
        ClearHint();
        return changed;
    }

    public void ClearHint()
    {
        HintFirstTileId = -1;
        HintSecondTileId = -1;
    }
}
