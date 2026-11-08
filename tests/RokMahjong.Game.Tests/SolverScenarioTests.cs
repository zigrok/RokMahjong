using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RokMahjong.Core;
using RokMahjong.Game;

namespace RokMahjong.Game.Tests;

public class SolverScenarioTests
{
    public static IEnumerable<object[]> LevelsAndDevices()
    {
        foreach (var level in Layouts.Levels)
            foreach (var device in new[] { InputDeviceKind.Mouse, InputDeviceKind.Touch, InputDeviceKind.Keyboard, InputDeviceKind.Gamepad })
                yield return new object[] { level.Index, device };
    }

    [Theory]
    [MemberData(nameof(LevelsAndDevices))]
    public void Solver_clears_every_level_through_real_input(int levelIndex, InputDeviceKind device)
    {
        var session = GameSession.Start(levelIndex);
        var board = new BoardController(session.Game, new BoardPresentation(session.Game.Layout, new Rectangle(0, 0, 1200, 800)));
        var source = new ScriptedInputSource();
        var router = new GameInputRouter { Board = board };
        var solution = DealGenerator.Generate(session.Game.Layout, session.Game.Deck, session.Game.Seed).Solution;

        router.Update(source, true, false, 1f, TimeSpan.Zero);
        foreach (var (firstId, secondId) in solution)
        {
            if (device is InputDeviceKind.Mouse or InputDeviceKind.Touch)
            {
                Click(source, router, board, firstId, device);
                Click(source, router, board, secondId, device);
            }
            else if (device == InputDeviceKind.Keyboard)
            {
                Focus(source, router, board, firstId, cycleKey: Keys.Tab);
                PressKey(source, router, Keys.Enter);
                Focus(source, router, board, secondId, cycleKey: Keys.Tab);
                PressKey(source, router, Keys.Enter);
            }
            else
            {
                Focus(source, router, board, firstId, cycleButton: Buttons.RightShoulder);
                PressGamepad(source, router, Buttons.A);
                Focus(source, router, board, secondId, cycleButton: Buttons.RightShoulder);
                PressGamepad(source, router, Buttons.A);
            }

            Assert.NotEqual(GameStatus.Deadlocked, session.Game.Status);
        }

        Assert.Equal(GameStatus.Won, session.Game.Status);
        Assert.Equal(0, session.Game.RemainingCount);
    }

    [Theory]
    [InlineData(390, 700)]
    [InlineData(900, 420)]
    public void Solver_clears_level_one_in_both_orientations(int width, int height)
    {
        var session = GameSession.Start(1);
        var board = new BoardController(session.Game, new BoardPresentation(session.Game.Layout, new Rectangle(0, 0, width, height)));
        var source = new ScriptedInputSource();
        var router = new GameInputRouter { Board = board };
        var solution = DealGenerator.Generate(session.Game.Layout, session.Game.Deck, session.Game.Seed).Solution;
        router.Update(source, true, false, 1f, TimeSpan.Zero);
        foreach (var (first, second) in solution)
        {
            Click(source, router, board, first, InputDeviceKind.Mouse);
            Click(source, router, board, second, InputDeviceKind.Mouse);
        }
        Assert.Equal(GameStatus.Won, session.Game.Status);
    }

    [Fact]
    public void Hint_shuffle_and_undo_are_reachable_through_keyboard_shortcuts()
    {
        var session = GameSession.Start(1);
        var board = new BoardController(session.Game, new BoardPresentation(session.Game.Layout, new Rectangle(0, 0, 1200, 800)));
        var source = new ScriptedInputSource();
        var router = new GameInputRouter { Board = board };
        var hintRequested = false;
        var shuffleRequested = false;
        var undoRequested = false;
        router.HintRequested += () => { hintRequested = true; board.Hint(); };
        router.ShuffleRequested += () => { shuffleRequested = true; board.Shuffle(new SeededRandom(4)); };
        router.UndoRequested += () => { undoRequested = true; board.Undo(); };

        router.Update(source, true, false, 1f, TimeSpan.Zero);
        PressKey(source, router, Keys.H);
        Assert.True(hintRequested);
        Assert.True(board.HintFirstTileId >= 0);

        PressKey(source, router, Keys.S);
        Assert.True(shuffleRequested);

        PressKey(source, router, Keys.U);
        Assert.True(undoRequested);
    }

    [Fact]
    public void Save_resume_then_finish_keeps_the_same_board()
    {
        var session = GameSession.Start(1);
        var board = new BoardController(session.Game, new BoardPresentation(session.Game.Layout, new Rectangle(0, 0, 1200, 800)));
        var source = new ScriptedInputSource();
        var router = new GameInputRouter { Board = board };
        var solution = DealGenerator.Generate(session.Game.Layout, session.Game.Deck, session.Game.Seed).Solution;
        router.Update(source, true, false, 1f, TimeSpan.Zero);

        for (var i = 0; i < 5; i++)
        {
            var (a, b) = solution[i];
            Click(source, router, board, a, InputDeviceKind.Mouse);
            Click(source, router, board, b, InputDeviceKind.Mouse);
        }

        var snapshot = session.Snapshot();
        var restored = GameSession.FromSnapshot(snapshot);
        var restoredBoard = new BoardController(restored.Game, new BoardPresentation(restored.Game.Layout, new Rectangle(0, 0, 1200, 800)));
        var restoredSource = new ScriptedInputSource();
        var restoredRouter = new GameInputRouter { Board = restoredBoard };
        restoredRouter.Update(restoredSource, true, false, 1f, TimeSpan.Zero);

        for (var i = 5; i < solution.Count; i++)
        {
            var (a, b) = solution[i];
            Click(restoredSource, restoredRouter, restoredBoard, a, InputDeviceKind.Mouse);
            Click(restoredSource, restoredRouter, restoredBoard, b, InputDeviceKind.Mouse);
        }

        Assert.Equal(GameStatus.Won, restored.Game.Status);
    }

    private static void Click(ScriptedInputSource source, GameInputRouter router, BoardController board, int tileId, InputDeviceKind kind)
    {
        var tile = board.Game.GetTile(tileId) ?? throw new InvalidOperationException($"Tile {tileId} missing.");
        var point = PointOnTile(board, tile);
        source.SetPointer(point, false, kind);
        router.Update(source, true, false, 1f, TimeSpan.Zero);
        source.SetPointer(point, true, kind);
        router.Update(source, true, false, 1f, TimeSpan.Zero);
        source.SetPointer(point, false, kind);
        router.Update(source, true, false, 1f, TimeSpan.Zero);
    }

    private static Point PointOnTile(BoardController board, Tile tile)
    {
        var rect = board.Presentation.GetFaceRect(tile);
        var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        if (board.Presentation.HitTest(board.Game.Tiles, center)?.Id == tile.Id) return center;
        for (var y = rect.Top + 4; y < rect.Bottom - 4; y += 4)
            for (var x = rect.Left + 4; x < rect.Right - 4; x += 4)
            {
                var point = new Point(x, y);
                if (board.Presentation.HitTest(board.Game.Tiles, point)?.Id == tile.Id) return point;
            }
        return center;
    }

    private static void Focus(ScriptedInputSource source, GameInputRouter router, BoardController board, int tileId, Keys? cycleKey = null, Buttons? cycleButton = null)
    {
        for (var i = 0; i < board.Game.FreeTiles().Count + 2 && board.FocusTileId != tileId; i++)
        {
            if (cycleKey.HasValue) PressKey(source, router, cycleKey.Value);
            else if (cycleButton.HasValue) PressGamepad(source, router, cycleButton.Value);
            else break;
        }
        Assert.Equal(tileId, board.FocusTileId);
    }

    private static void PressKey(ScriptedInputSource source, GameInputRouter router, Keys key)
    {
        source.PressKey(key);
        router.Update(source, true, false, 1f, TimeSpan.Zero);
        source.ReleaseKey(key);
        router.Update(source, true, false, 1f, TimeSpan.Zero);
    }

    private static void PressGamepad(ScriptedInputSource source, GameInputRouter router, Buttons button)
    {
        source.PressGamepadButton(button);
        router.Update(source, true, false, 1f, TimeSpan.Zero);
        source.ReleaseGamepadButton(button);
        router.Update(source, true, false, 1f, TimeSpan.Zero);
    }
}
