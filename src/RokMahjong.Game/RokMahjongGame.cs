using System.Reflection;
using Forma;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RokMahjong.Core;

namespace RokMahjong.Game;

public sealed class RokMahjongGame : Microsoft.Xna.Framework.Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly UIContext _ui;
    private IInputSource _input = new DeviceInputSource();
    private readonly GameInputRouter _router;
    private readonly string? _screenshotPath;
    private readonly string _startScreen;
    private readonly int _autoplayLevel;
    private readonly int _startLevel = 1;
    private ScriptedInputSource? _autoplayInput;
    private IReadOnlyList<(int First, int Second)>? _autoplaySolution;
    private int _autoplayPair;
    private int _autoplayTile;
    private int _autoplayPhase;
    private TimeSpan _autoplayNext;
    private UIFontFace? _fontFace;
    private TileAtlas? _tileAtlas;
    private GameShell? _shell;
    private int _frames;

    public RokMahjongGame(string[] args)
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 800,
        };
        _graphics.AllowHighDpi = true;
        IsMouseVisible = true;
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
        Window.AllowUserResizing = true;
        Window.Title = "Rok Mahjong";

        _ui = new UIContext { SuppressPolledInput = true };
        _router = new GameInputRouter(_ui);

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--screenshot" && i + 1 < args.Length)
                _screenshotPath = args[++i];
            else if (args[i] == "--screen" && i + 1 < args.Length)
                _startScreen = args[++i];
            else if (args[i] == "--autoplay" && i + 1 < args.Length)
                _autoplayLevel = int.Parse(args[++i]);
            else if (args[i] == "--level" && i + 1 < args.Length)
                _startLevel = int.Parse(args[++i]);
        }

        _startScreen ??= "menu";
    }

    public UIContext Ui => _ui;
    public GameShell? Shell => _shell;

    protected override void LoadContent()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("RokMahjong.Game.Inter_Regular.ttf")
            ?? throw new InvalidOperationException("Embedded UI font is missing.");
        _fontFace = UIFontFace.FromStream(stream);
        var body = new DynamicUIFont(_fontFace, 20);
        var title = new DynamicUIFont(_fontFace, 30);
        var tile = new DynamicUIFont(_fontFace, 20);
        var family = new UIFontFamily(new UIFont[] { body, title, tile });

        _ui.Theme = new Theme
        {
            FontFamily = family,
            FontSize = 20,
            BackgroundColor = new Color(6, 20, 17),
            PanelColor = new Color(12, 45, 38),
            PanelBorderColor = new Color(88, 176, 145, 130),
            TextColor = new Color(228, 240, 232),
            DisabledTextColor = new Color(108, 132, 120),
            AccentColor = new Color(83, 190, 155),
            HoverColor = new Color(28, 84, 68),
            PressedColor = new Color(17, 62, 52),
            FocusColor = new Color(236, 201, 123),
            Separation = 8,
            BorderWidth = 1,
        };
        _ui.TooltipUIFont = body;

        try
        {
            _tileAtlas = TileAtlas.Load(GraphicsDevice, typeof(RokMahjongGame).Assembly);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Tile atlas could not be loaded; falling back to vector tiles. {exception.Message}");
        }

        _shell = new GameShell(_ui, new SaveStore(), body, title, tile, _tileAtlas);
        _shell.ExitRequested += Exit;

        _router.BoardChanged += () => _shell?.OnBoardChanged();
        _router.PauseRequested += () => _shell?.Pause();
        _router.CancelRequested += () => _shell?.Cancel();
        _router.HintRequested += () => _shell?.UseHint();
        _router.ShuffleRequested += () => _shell?.Shuffle();
        _router.UndoRequested += () => _shell?.Undo();

        if (_autoplayLevel > 0)
        {
            _shell.StartLevel(_autoplayLevel);
            _autoplayInput = new ScriptedInputSource();
            _input = _autoplayInput;
        }
        else
        {
            switch (_startScreen)
            {
                case "game":
                    _shell.StartLevel(_startLevel);
                    break;
                case "levels":
                    _shell.ShowLevelSelect();
                    break;
                case "settings":
                    _shell.ShowSettings();
                    break;
                case "howto":
                    _shell.ShowHowToPlay();
                    break;
                default:
                    _shell.ShowMainMenu();
                    break;
            }
        }

        base.LoadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        if (_shell is null) return;
        var viewport = GraphicsDevice.Viewport;
        var clientWidth = Math.Max(1, Window.ClientBounds.Width);
        var scale = viewport.Width / (float)clientWidth;
        if (!float.IsFinite(scale) || scale <= 0) scale = 1f;
        _ui.DisplayScale = scale;
        var logical = new Vector2(viewport.Width, viewport.Height) / scale;
        _ui.ViewportSize = logical;

        _router.Board = _shell.Board;
        AdvanceAutoplay(gameTime);
        _router.Update(_input, _shell.IsGameplay, _shell.IsModal, scale, gameTime.ElapsedGameTime);
        _shell.Update(gameTime, logical);
        _ui.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(5, 18, 15));
        _ui.Draw(GraphicsDevice);
        _frames++;
        if (_screenshotPath is not null && _frames >= 15)
        {
            SaveScreenshot(_screenshotPath);
            Exit();
        }
        base.Draw(gameTime);
    }

    private void AdvanceAutoplay(GameTime gameTime)
    {
        if (_autoplayInput is null || _shell?.Board is null) return;
        if (gameTime.TotalGameTime < _autoplayNext) return;
        var board = _shell.Board;
        _autoplaySolution ??= DealGenerator.Generate(board.Game.Layout, board.Game.Deck, board.Game.Seed).Solution;
        if (_autoplayPair >= _autoplaySolution.Count)
        {
            _autoplayInput = null;
            _input = new DeviceInputSource();
            return;
        }

        var pair = _autoplaySolution[_autoplayPair];
        var tileId = _autoplayTile == 0 ? pair.First : pair.Second;
        var tile = board.Game.GetTile(tileId);
        if (tile is null) { _autoplayPair++; _autoplayTile = 0; return; }
        var rect = board.Presentation.GetFaceRect(tile);
        var point = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

        switch (_autoplayPhase)
        {
            case 0:
                _autoplayInput.SetPointer(point, false, InputDeviceKind.Mouse);
                _autoplayPhase = 1;
                _autoplayNext = gameTime.TotalGameTime + TimeSpan.FromMilliseconds(90);
                break;
            case 1:
                _autoplayInput.SetPointer(point, true, InputDeviceKind.Mouse);
                _autoplayPhase = 2;
                _autoplayNext = gameTime.TotalGameTime + TimeSpan.FromMilliseconds(90);
                break;
            default:
                _autoplayInput.SetPointer(point, false, InputDeviceKind.Mouse);
                _autoplayPhase = 0;
                _autoplayTile++;
                if (_autoplayTile >= 2)
                {
                    _autoplayTile = 0;
                    _autoplayPair++;
                }
                _autoplayNext = gameTime.TotalGameTime + TimeSpan.FromMilliseconds(180);
                break;
        }
    }

    private void SaveScreenshot(string path)
    {
        var viewport = GraphicsDevice.Viewport;
        var pixels = new Color[viewport.Width * viewport.Height];
        GraphicsDevice.GetBackBufferData(pixels);
        using var texture = new Texture2D(GraphicsDevice, viewport.Width, viewport.Height);
        texture.SetData(pixels);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        texture.SaveAsPng(stream, viewport.Width, viewport.Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tileAtlas?.Dispose();
            _ui.Dispose();
            _fontFace?.Dispose();
        }
        base.Dispose(disposing);
    }
}
