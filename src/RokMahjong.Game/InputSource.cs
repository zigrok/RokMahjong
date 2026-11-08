using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;

namespace RokMahjong.Game;

public enum InputDeviceKind
{
    Mouse,
    Touch,
    Keyboard,
    Gamepad,
}

public readonly record struct PointerSnapshot(Point PhysicalPosition, bool Down, InputDeviceKind Kind);

public readonly record struct GamepadSnapshot(
    bool Connected,
    bool DPadUp,
    bool DPadDown,
    bool DPadLeft,
    bool DPadRight,
    bool A,
    bool B,
    bool X,
    bool Y,
    bool LeftShoulder,
    bool RightShoulder,
    bool Start,
    Vector2 LeftStick)
{
    public static GamepadSnapshot Empty => new(false, false, false, false, false, false, false, false, false, false, false, false, Vector2.Zero);

    public static GamepadSnapshot From(GamePadState state) => new(
        state.IsConnected,
        state.DPad.Up == ButtonState.Pressed,
        state.DPad.Down == ButtonState.Pressed,
        state.DPad.Left == ButtonState.Pressed,
        state.DPad.Right == ButtonState.Pressed,
        state.Buttons.A == ButtonState.Pressed,
        state.Buttons.B == ButtonState.Pressed,
        state.Buttons.X == ButtonState.Pressed,
        state.Buttons.Y == ButtonState.Pressed,
        state.Buttons.LeftShoulder == ButtonState.Pressed,
        state.Buttons.RightShoulder == ButtonState.Pressed,
        state.Buttons.Start == ButtonState.Pressed,
        state.ThumbSticks.Left);
}

public interface IInputSource
{
    void BeginFrame();
    PointerSnapshot Pointer { get; }
    KeyboardState Keyboard { get; }
    GamepadSnapshot Gamepad { get; }
    InputDeviceKind LastDevice { get; }
}

public sealed class DeviceInputSource : IInputSource
{
    private MouseState _mouse;
    private KeyboardState _keyboard;
    private GamepadSnapshot _gamepad;
    private PointerSnapshot _pointer;
    private bool _touchWasDown;
    private Point _lastTouch;
    private InputDeviceKind _lastDevice = InputDeviceKind.Mouse;

    public PointerSnapshot Pointer => _pointer;
    public KeyboardState Keyboard => _keyboard;
    public GamepadSnapshot Gamepad => _gamepad;
    public InputDeviceKind LastDevice => _lastDevice;

    public void BeginFrame()
    {
        var previousMouse = _mouse;
        var previousKeyboard = _keyboard;
        var previousGamepad = _gamepad;

        _mouse = Microsoft.Xna.Framework.Input.Mouse.GetState();
        _keyboard = Microsoft.Xna.Framework.Input.Keyboard.GetState();
        _gamepad = GamepadSnapshot.From(Microsoft.Xna.Framework.Input.GamePad.GetState(PlayerIndex.One));

        var touch = TouchPanel.GetState();
        if (touch.Count > 0)
        {
            var location = touch[0];
            _lastTouch = location.Position.ToPoint();
            _touchWasDown = location.State != TouchLocationState.Released;
            _pointer = new PointerSnapshot(_lastTouch, _touchWasDown, InputDeviceKind.Touch);
            _lastDevice = InputDeviceKind.Touch;
        }
        else if (_touchWasDown)
        {
            _touchWasDown = false;
            _pointer = new PointerSnapshot(_lastTouch, false, InputDeviceKind.Touch);
            _lastDevice = InputDeviceKind.Touch;
        }
        else
        {
            _pointer = new PointerSnapshot(_mouse.Position, _mouse.LeftButton == ButtonState.Pressed, InputDeviceKind.Mouse);
            if (_mouse.Position != previousMouse.Position || _mouse.LeftButton != previousMouse.LeftButton)
                _lastDevice = InputDeviceKind.Mouse;
        }

        if (_keyboard.GetPressedKeys().Length > 0 && !_keyboard.Equals(previousKeyboard))
            _lastDevice = InputDeviceKind.Keyboard;
        if (_gamepad.Connected && !_gamepad.Equals(previousGamepad))
            _lastDevice = InputDeviceKind.Gamepad;
    }
}

public sealed class ScriptedInputSource : IInputSource
{
    private readonly HashSet<Keys> _keys = new();
    private PointerSnapshot _pointer;
    private GamepadSnapshot _gamepad = GamepadSnapshot.Empty;
    private InputDeviceKind _lastDevice = InputDeviceKind.Mouse;

    public PointerSnapshot Pointer => _pointer;
    public KeyboardState Keyboard => new(_keys.ToArray());
    public GamepadSnapshot Gamepad => _gamepad;
    public InputDeviceKind LastDevice => _lastDevice;

    public void BeginFrame() { }

    public void SetPointer(Point position, bool down, InputDeviceKind kind = InputDeviceKind.Mouse)
    {
        _pointer = new PointerSnapshot(position, down, kind);
        _lastDevice = kind;
    }

    public void MovePointer(Point position, InputDeviceKind kind = InputDeviceKind.Mouse)
    {
        _pointer = new PointerSnapshot(position, _pointer.Down, kind);
        _lastDevice = kind;
    }

    public void SetPointerDown(bool down) => _pointer = _pointer with { Down = down };

    public void SetKey(Keys key, bool down)
    {
        if (down) _keys.Add(key); else _keys.Remove(key);
        _lastDevice = InputDeviceKind.Keyboard;
    }

    public void PressKey(Keys key) => SetKey(key, true);
    public void ReleaseKey(Keys key) => SetKey(key, false);
    public void ClearKeys() => _keys.Clear();

    public void SetGamepad(GamepadSnapshot gamepad)
    {
        _gamepad = gamepad;
        _lastDevice = InputDeviceKind.Gamepad;
    }

    public void PressGamepadButton(Buttons button)
    {
        _gamepad = _gamepad with
        {
            Connected = true,
            A = _gamepad.A || button == Buttons.A,
            B = _gamepad.B || button == Buttons.B,
            X = _gamepad.X || button == Buttons.X,
            Y = _gamepad.Y || button == Buttons.Y,
            LeftShoulder = _gamepad.LeftShoulder || button == Buttons.LeftShoulder,
            RightShoulder = _gamepad.RightShoulder || button == Buttons.RightShoulder,
            Start = _gamepad.Start || button == Buttons.Start,
            DPadUp = _gamepad.DPadUp || button == Buttons.DPadUp,
            DPadDown = _gamepad.DPadDown || button == Buttons.DPadDown,
            DPadLeft = _gamepad.DPadLeft || button == Buttons.DPadLeft,
            DPadRight = _gamepad.DPadRight || button == Buttons.DPadRight,
        };
        _lastDevice = InputDeviceKind.Gamepad;
    }

    public void ReleaseGamepadButton(Buttons button)
    {
        _gamepad = _gamepad with
        {
            A = button == Buttons.A ? false : _gamepad.A,
            B = button == Buttons.B ? false : _gamepad.B,
            X = button == Buttons.X ? false : _gamepad.X,
            Y = button == Buttons.Y ? false : _gamepad.Y,
            LeftShoulder = button == Buttons.LeftShoulder ? false : _gamepad.LeftShoulder,
            RightShoulder = button == Buttons.RightShoulder ? false : _gamepad.RightShoulder,
            Start = button == Buttons.Start ? false : _gamepad.Start,
            DPadUp = button == Buttons.DPadUp ? false : _gamepad.DPadUp,
            DPadDown = button == Buttons.DPadDown ? false : _gamepad.DPadDown,
            DPadLeft = button == Buttons.DPadLeft ? false : _gamepad.DPadLeft,
            DPadRight = button == Buttons.DPadRight ? false : _gamepad.DPadRight,
        };
        _lastDevice = InputDeviceKind.Gamepad;
    }
}
