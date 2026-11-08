using Forma;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace RokMahjong.Game;

public sealed class GameInputRouter
{
    private readonly UIContext? _ui;
    private readonly HashSet<Keys> _previousKeys = new();
    private PointerSnapshot _previousPointer;
    private GamepadSnapshot _previousGamepad = GamepadSnapshot.Empty;
    private Keys? _previousStickDirection;
    private bool _boardCapture;

    public GameInputRouter(UIContext? ui = null)
    {
        _ui = ui;
    }

    public BoardController? Board { get; set; }
    public event Action? PauseRequested;
    public event Action? HintRequested;
    public event Action? ShuffleRequested;
    public event Action? UndoRequested;
    public event Action? CancelRequested;
    public event Action? BoardChanged;

    public void Update(IInputSource source, bool gameplayActive, bool modalActive, float displayScale, TimeSpan elapsed)
    {
        source.BeginFrame();
        if (displayScale <= 0.0001f) displayScale = 1f;

        if (_ui is not null)
        {
            _ui.InputModality = source.LastDevice switch
            {
                InputDeviceKind.Touch => InputModality.Touch,
                InputDeviceKind.Keyboard => InputModality.Keyboard,
                InputDeviceKind.Gamepad => InputModality.Gamepad,
                _ => InputModality.Pointer,
            };
        }

        UpdatePointer(source, gameplayActive, modalActive, displayScale);
        UpdateKeyboard(source, gameplayActive, modalActive);
        UpdateGamepad(source, gameplayActive, modalActive);
    }

    private void UpdatePointer(IInputSource source, bool gameplayActive, bool modalActive, float displayScale)
    {
        var pointer = source.Pointer;
        var logical = new Point(
            (int)MathF.Round(pointer.PhysicalPosition.X / displayScale),
            (int)MathF.Round(pointer.PhysicalPosition.Y / displayScale));
        var pressed = pointer.Down && !_previousPointer.Down;
        var released = !pointer.Down && _previousPointer.Down;
        var moved = pointer.PhysicalPosition != _previousPointer.PhysicalPosition;
        var boardEligible = gameplayActive && !modalActive && Board is not null;
        var overBoard = boardEligible && Board!.Contains(logical);

        if (boardEligible && (overBoard || _boardCapture))
        {
            if (pressed)
            {
                Board!.PointerPressed(logical);
                _boardCapture = true;
                BoardChanged?.Invoke();
            }
            else if (moved)
            {
                Board!.PointerMoved(logical);
                if (_boardCapture) BoardChanged?.Invoke();
            }
            else if (released)
            {
                Board!.PointerReleased(logical);
                _boardCapture = false;
                BoardChanged?.Invoke();
            }
        }
        else
        {
            if (_ui is not null)
            {
                if (moved) _ui.InjectPointerMove(pointer.PhysicalPosition);
                if (pressed)
                {
                    _ui.InjectPointerMove(pointer.PhysicalPosition);
                    _ui.InjectPointerPress(pointer.PhysicalPosition);
                }
                if (released) _ui.InjectPointerRelease(pointer.PhysicalPosition);
            }
        }

        if (released) _boardCapture = false;
        _previousPointer = pointer;
    }

    private void UpdateKeyboard(IInputSource source, bool gameplayActive, bool modalActive)
    {
        var keyboard = source.Keyboard;
        var pressed = keyboard.GetPressedKeys();
        foreach (var key in pressed)
        {
            if (_previousKeys.Contains(key)) continue;
            if (gameplayActive && !modalActive && Board is not null)
                HandleGameplayKey(key);
            else
                InjectKey(key, keyboard);
        }
        foreach (var key in _previousKeys)
        {
            if (keyboard.IsKeyDown(key)) continue;
            if (!(gameplayActive && !modalActive) && _ui is not null)
                _ui.InjectKeyRelease(key);
        }

        _previousKeys.Clear();
        foreach (var key in pressed) _previousKeys.Add(key);
    }

    private void HandleGameplayKey(Keys key)
    {
        switch (key)
        {
            case Keys.Left:
                Board?.MoveFocus(Keys.Left);
                break;
            case Keys.Right:
                Board?.MoveFocus(Keys.Right);
                break;
            case Keys.Up:
                Board?.MoveFocus(Keys.Up);
                break;
            case Keys.Down:
                Board?.MoveFocus(Keys.Down);
                break;
            case Keys.Tab:
                Board?.CycleFocus();
                break;
            case Keys.Enter:
            case Keys.Space:
                Board?.ActivateFocus();
                BoardChanged?.Invoke();
                break;
            case Keys.Escape:
                if (Board?.Game.SelectedTileId >= 0) Board.CancelSelection();
                else CancelRequested?.Invoke();
                break;
            case Keys.H:
                HintRequested?.Invoke();
                break;
            case Keys.S:
                ShuffleRequested?.Invoke();
                break;
            case Keys.U:
            case Keys.Z:
                UndoRequested?.Invoke();
                break;
            case Keys.P:
                PauseRequested?.Invoke();
                break;
        }
    }

    private void UpdateGamepad(IInputSource source, bool gameplayActive, bool modalActive)
    {
        var gamepad = source.Gamepad;
        if (!gamepad.Connected)
        {
            _previousGamepad = gamepad;
            return;
        }

        var stickDirection = EdgeStickDirection(gamepad);
        if (gameplayActive && !modalActive && Board is not null)
        {
            if (Pressed(gamepad, _previousGamepad, gp => gp.DPadUp) || stickDirection == Keys.Up)
                Board.MoveFocus(Keys.Up);
            if (Pressed(gamepad, _previousGamepad, gp => gp.DPadDown) || stickDirection == Keys.Down)
                Board.MoveFocus(Keys.Down);
            if (Pressed(gamepad, _previousGamepad, gp => gp.DPadLeft) || stickDirection == Keys.Left)
                Board.MoveFocus(Keys.Left);
            if (Pressed(gamepad, _previousGamepad, gp => gp.DPadRight) || stickDirection == Keys.Right)
                Board.MoveFocus(Keys.Right);
            if (Pressed(gamepad, _previousGamepad, gp => gp.RightShoulder)) Board.CycleFocus();
            if (Pressed(gamepad, _previousGamepad, gp => gp.A))
            {
                Board.ActivateFocus();
                BoardChanged?.Invoke();
            }
            if (Pressed(gamepad, _previousGamepad, gp => gp.B))
            {
                if (Board.Game.SelectedTileId >= 0) Board.CancelSelection();
                else PauseRequested?.Invoke();
            }
            if (Pressed(gamepad, _previousGamepad, gp => gp.X)) HintRequested?.Invoke();
            if (Pressed(gamepad, _previousGamepad, gp => gp.Y)) ShuffleRequested?.Invoke();
            if (Pressed(gamepad, _previousGamepad, gp => gp.LeftShoulder)) UndoRequested?.Invoke();
            if (Pressed(gamepad, _previousGamepad, gp => gp.Start)) PauseRequested?.Invoke();
        }
        else
        {
            if (Pressed(gamepad, _previousGamepad, gp => gp.DPadUp) || stickDirection == Keys.Up) InjectSyntheticKey(Keys.Up);
            if (Pressed(gamepad, _previousGamepad, gp => gp.DPadDown) || stickDirection == Keys.Down) InjectSyntheticKey(Keys.Down);
            if (Pressed(gamepad, _previousGamepad, gp => gp.DPadLeft) || stickDirection == Keys.Left) InjectSyntheticKey(Keys.Left);
            if (Pressed(gamepad, _previousGamepad, gp => gp.DPadRight) || stickDirection == Keys.Right) InjectSyntheticKey(Keys.Right);
            if (Pressed(gamepad, _previousGamepad, gp => gp.A)) InjectSyntheticKey(Keys.Enter);
            if (Pressed(gamepad, _previousGamepad, gp => gp.B) || Pressed(gamepad, _previousGamepad, gp => gp.Start))
                InjectSyntheticKey(Keys.Escape);
        }

        _previousGamepad = gamepad;
    }

    private Keys? EdgeStickDirection(GamepadSnapshot gamepad)
    {
        var stick = gamepad.LeftStick;
        Keys? current = null;
        if (stick.Length() >= 0.55f)
        {
            if (MathF.Abs(stick.X) > MathF.Abs(stick.Y))
                current = stick.X < 0 ? Keys.Left : Keys.Right;
            else
                current = stick.Y < 0 ? Keys.Up : Keys.Down;
        }

        var edge = current.HasValue && _previousStickDirection != current ? current : null;
        _previousStickDirection = current;
        return edge;
    }

    private static bool Pressed(GamepadSnapshot current, GamepadSnapshot previous, Func<GamepadSnapshot, bool> read) =>
        read(current) && !read(previous);

    private void InjectKey(Keys key, KeyboardState keyboard)
    {
        if (_ui is null) return;
        var modifiers = new List<Keys>();
        if (keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift)) modifiers.Add(Keys.LeftShift);
        if (keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl)) modifiers.Add(Keys.LeftControl);
        if (keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt)) modifiers.Add(Keys.LeftAlt);
        _ui.InjectKeyPress(key, modifiers.ToArray());
    }

    private void InjectSyntheticKey(Keys key)
    {
        if (_ui is null) return;
        _ui.InjectKeyPress(key);
        _ui.InjectKeyRelease(key);
    }
}
