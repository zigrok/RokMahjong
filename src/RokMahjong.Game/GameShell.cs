using Forma;
using Microsoft.Xna.Framework;
using RokMahjong.Core;

namespace RokMahjong.Game;

public enum ScreenMode
{
    MainMenu,
    LevelSelect,
    Playing,
    Paused,
    Settings,
    HowToPlay,
    Result,
    Deadlock,
}

public sealed class GameShell
{
    private readonly UIContext _ui;
    private readonly SaveStore _store;
    private readonly ColorRect _background;
    private readonly UIFont _bodyFont;
    private readonly UIFont _titleFont;
    private readonly Container _root;
    private readonly MahjongBoardView _boardView;
    private readonly Panel _topBar;
    private readonly Panel _bottomBar;
    private readonly HBoxContainer _topBarRow;
    private readonly HBoxContainer _bottomBarRow;
    private readonly Label _titleLabel;
    private readonly Label _scoreLabel;
    private readonly Label _comboLabel;
    private readonly Label _statusLabel;
    private readonly Button _undoButton;
    private readonly Button _hintButton;
    private readonly Button _shuffleButton;
    private readonly Button _pauseButton;
    private readonly ColorRect _scrim;
    private readonly CenterContainer _overlayCenter;
    private readonly Border _overlayCard;
    private readonly VBoxContainer _overlayBox;

    private AppSettings _settings;
    private ProgressState _progress;
    private ScreenMode _mode = ScreenMode.MainMenu;
    private ScreenMode _settingsReturn = ScreenMode.MainMenu;
    private GameSession? _session;
    private BoardController? _board;
    private TimeSpan _hintUntil;
    private GameTime _now = new(TimeSpan.Zero, TimeSpan.Zero);
    private Rectangle _boardRect;
    private bool _resultShown;
    private int _automationCounter;

    public GameShell(UIContext ui, SaveStore store, UIFont bodyFont, UIFont titleFont, UIFont tileFont, TileAtlas? tileAtlas = null)
    {
        _ui = ui;
        _store = store;
        _bodyFont = bodyFont;
        _titleFont = titleFont;
        _settings = store.LoadSettings();
        _progress = store.LoadProgress();

        _root = new Container { Size = new Vector2(1280, 800) };
        _background = new ColorRect
        {
            Color = new Color(8, 32, 27),
            MouseFilter = MouseFilter.Ignore,
        };
        _boardView = new MahjongBoardView(tileFont, tileAtlas)
        {
            Position = Vector2.Zero,
            Size = new Vector2(1280, 800),
            Visible = false,
        };

        _topBar = new Panel
        {
            BackgroundColor = new Color(7, 27, 23, 235),
            BorderColor = new Color(88, 176, 145, 110),
            BorderWidth = 1,
            Visible = false,
        };
        _topBarRow = new HBoxContainer { Separation = 12 };
        _titleLabel = new Label
        {
            Text = "Rok Mahjong",
            FontSize = 24,
            FontColor = new Color(239, 213, 154),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalSizeFlags = SizeFlags.Expand | SizeFlags.Fill,
        };
        _scoreLabel = new Label
        {
            Text = "Score 0",
            FontSize = 20,
            FontColor = new Color(226, 240, 231),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _comboLabel = new Label
        {
            Text = "×1",
            FontSize = 20,
            FontColor = new Color(247, 196, 94),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var topMenu = MakeButton("Menu", BackToMenu, 86, 38);
        var topSettings = MakeButton("Settings", ShowSettings, 110, 38);
        _topBarRow.AddChild(topMenu);
        _topBarRow.AddChild(_titleLabel);
        _topBarRow.AddChild(_scoreLabel);
        _topBarRow.AddChild(_comboLabel);
        _topBarRow.AddChild(topSettings);
        _topBar.AddChild(_topBarRow);

        _bottomBar = new Panel
        {
            BackgroundColor = new Color(7, 27, 23, 235),
            BorderColor = new Color(88, 176, 145, 110),
            BorderWidth = 1,
            Visible = false,
        };
        _bottomBarRow = new HBoxContainer { Separation = 10 };
        _undoButton = MakeButton("Undo", Undo, 100, 46);
        _hintButton = MakeButton("Hint", UseHint, 100, 46);
        _shuffleButton = MakeButton("Shuffle", Shuffle, 110, 46);
        _pauseButton = MakeButton("Pause", Pause, 100, 46);
        _statusLabel = new Label
        {
            Text = "",
            FontSize = 18,
            FontColor = new Color(186, 214, 200),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalSizeFlags = SizeFlags.Expand | SizeFlags.Fill,
        };
        _bottomBarRow.AddChild(_undoButton);
        _bottomBarRow.AddChild(_hintButton);
        _bottomBarRow.AddChild(_shuffleButton);
        _bottomBarRow.AddChild(_statusLabel);
        _bottomBarRow.AddChild(_pauseButton);
        _bottomBar.AddChild(_bottomBarRow);

        _scrim = new ColorRect
        {
            Color = new Color(0, 0, 0, 160),
            Visible = false,
            MouseFilter = MouseFilter.Stop,
        };
        _overlayCenter = new CenterContainer
        {
            Visible = false,
            MouseFilter = MouseFilter.Stop,
        };
        _overlayBox = new VBoxContainer
        {
            Separation = 12,
            CustomMinimumSize = new Vector2(440, 0),
        };
        _overlayCard = new Border
        {
            Background = new SolidColorBrush(new Color(9, 36, 31, 248)),
            BorderBrush = new SolidColorBrush(new Color(91, 180, 148, 150)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(28),
            CornerRadius = new CornerRadius(10),
        };
        _overlayCard.AddChild(_overlayBox);
        _overlayCenter.AddChild(_overlayCard);

        _root.AddChild(_background);
        _root.AddChild(_boardView);
        _root.AddChild(_topBar);
        _root.AddChild(_bottomBar);
        _root.AddChild(_scrim);
        _root.AddChild(_overlayCenter);
        _ui.Add(_root);
    }

    public Control Root => _root;
    public ScreenMode Mode => _mode;
    public GameSession? Session => _session;
    public BoardController? Board => _board;
    public MahjongBoardView BoardView => _boardView;
    public AppSettings Settings => _settings;
    public bool IsGameplay => _mode == ScreenMode.Playing;
    public bool IsModal => _mode is not ScreenMode.Playing;
    public event Action? ExitRequested;

    public void Update(GameTime gameTime, Vector2 viewport)
    {
        _now = gameTime;
        Arrange(viewport);
        _boardView.Now = gameTime.TotalGameTime;
        _boardView.HighlightFreeTiles = _settings.HighlightFreeTiles;
        _boardView.ReducedMotion = _settings.ReducedMotion;

        if (_hintUntil != TimeSpan.Zero && gameTime.TotalGameTime > _hintUntil)
        {
            _board?.ClearHint();
            _hintUntil = TimeSpan.Zero;
        }

        if (_session is not null && _mode == ScreenMode.Playing)
        {
            if (_session.Game.Status == GameStatus.Won)
            {
                ShowResult();
            }
            else if (_session.Game.Status == GameStatus.Deadlocked)
            {
                ShowDeadlock();
            }
        }

        SyncHud();
    }

    public void Arrange(Vector2 viewport)
    {
        _root.Size = viewport;
        _background.Position = Vector2.Zero;
        _background.Size = viewport;
        _boardView.Position = Vector2.Zero;
        _boardView.Size = viewport;

        var topHeight = viewport.Y < 620 ? 54f : 64f;
        var bottomHeight = viewport.Y < 620 ? 64f : 78f;
        _topBar.Position = Vector2.Zero;
        _topBar.Size = new Vector2(viewport.X, topHeight);
        _topBarRow.Position = new Vector2(12, 7);
        _topBarRow.Size = new Vector2(MathF.Max(0, viewport.X - 24), MathF.Max(0, topHeight - 14));

        _bottomBar.Position = new Vector2(0, MathF.Max(0, viewport.Y - bottomHeight));
        _bottomBar.Size = new Vector2(viewport.X, bottomHeight);
        _bottomBarRow.Position = new Vector2(12, 8);
        _bottomBarRow.Size = new Vector2(MathF.Max(0, viewport.X - 24), MathF.Max(0, bottomHeight - 16));

        var boardRect = new Rectangle(
            12,
            (int)topHeight + 12,
            Math.Max(1, (int)viewport.X - 24),
            Math.Max(1, (int)viewport.Y - (int)topHeight - (int)bottomHeight - 24));
        _boardRect = boardRect;
        if (_board is not null && _board.Presentation.AvailableRect != boardRect)
            _board.SetPresentation(new BoardPresentation(_board.Game.Layout, boardRect));

        _scrim.Position = Vector2.Zero;
        _scrim.Size = viewport;
        _overlayCenter.Position = Vector2.Zero;
        _overlayCenter.Size = viewport;
    }

    public void StartLevel(int index)
    {
        _session = GameSession.Start(index);
        _resultShown = false;
        _mode = ScreenMode.Playing;
        _topBar.Visible = true;
        _bottomBar.Visible = true;
        _boardView.Visible = true;
        HideOverlay();
        Arrange(_ui.ViewportSize);
        _board = new BoardController(_session.Game, new BoardPresentation(_session.Game.Layout, _boardRect));
        _boardView.SetBoard(_board);
        _store.SaveSession(_session);
        SyncHud();
    }

    public void ContinueGame()
    {
        var snapshot = _store.LoadSession();
        if (snapshot is null)
        {
            StartLevel(1);
            return;
        }

        _session = GameSession.FromSnapshot(snapshot);
        _resultShown = false;
        _mode = ScreenMode.Playing;
        _topBar.Visible = true;
        _bottomBar.Visible = true;
        _boardView.Visible = true;
        HideOverlay();
        Arrange(_ui.ViewportSize);
        _board = new BoardController(_session.Game, new BoardPresentation(_session.Game.Layout, _boardRect));
        _boardView.SetBoard(_board);
        SyncHud();
    }

    public void Pause()
    {
        if (_mode != ScreenMode.Playing) return;
        _mode = ScreenMode.Paused;
        ShowOverlay(BuildPauseMenu());
    }

    public void Resume()
    {
        if (_mode != ScreenMode.Paused) return;
        _mode = ScreenMode.Playing;
        HideOverlay();
    }

    public void Restart()
    {
        var index = _session?.Level.Index ?? 1;
        StartLevel(index);
    }

    public void UseHint()
    {
        if (_board is null || _mode != ScreenMode.Playing) return;
        var hint = _board.Hint();
        _hintUntil = _now.TotalGameTime + TimeSpan.FromSeconds(5);
        _statusLabel.Text = hint.HasHint ? "Hint: match the glowing tiles" : "No moves — shuffle or undo";
    }

    public void Shuffle()
    {
        if (_board is null || _mode != ScreenMode.Playing) return;
        var result = _board.Shuffle(new SeededRandom(Environment.TickCount ^ _session!.Level.Index * 7919));
        _statusLabel.Text = result.Changed ? "Tiles shuffled" : "Could not shuffle";
        SaveSession();
    }

    public void Undo()
    {
        if (_board is null || _mode != ScreenMode.Playing) return;
        _statusLabel.Text = _board.Undo() ? "Undone" : "Nothing to undo";
        SaveSession();
    }

    public void Cancel()
    {
        if (_mode == ScreenMode.Playing)
        {
            _board?.CancelSelection();
            return;
        }
        if (_mode == ScreenMode.Paused)
        {
            Resume();
            return;
        }
        if (_mode is ScreenMode.Settings or ScreenMode.HowToPlay)
        {
            if (_settingsReturn == ScreenMode.Paused) Pause();
            else ShowMainMenu();
            return;
        }
        if (_mode is ScreenMode.LevelSelect or ScreenMode.Result or ScreenMode.Deadlock)
            BackToMenu();
    }

    public void SaveSession()
    {
        if (_session is not null && _session.Game.Status == GameStatus.Playing) _store.SaveSession(_session);
    }

    public void OnBoardChanged()
    {
        if (_board is null) return;
        _boardView.SetBoard(_board);
        SaveSession();
    }

    public void ShowMainMenu()
    {
        _mode = ScreenMode.MainMenu;
        _session = null;
        _board = null;
        _boardView.SetBoard(null);
        _boardView.Visible = false;
        _topBar.Visible = false;
        _bottomBar.Visible = false;
        var box = new VBoxContainer { Separation = 12, CustomMinimumSize = new Vector2(420, 0) };
        box.AddChild(MakeTitle("Rok Mahjong"));
        box.AddChild(MakeCaption("A calm mahjong solitaire journey"));
        box.AddChild(MakeButton("Play", ShowLevelSelect, 360, 50));
        if (_store.LoadSession() is not null) box.AddChild(MakeButton("Continue", ContinueGame, 360, 50));
        box.AddChild(MakeButton("How to Play", ShowHowToPlay, 360, 46));
        box.AddChild(MakeButton("Settings", ShowSettings, 360, 46));
        box.AddChild(MakeButton("Quit", () => ExitRequested?.Invoke(), 360, 42));
        ShowOverlay(box);
    }

    public void ShowLevelSelect()
    {
        _mode = ScreenMode.LevelSelect;
        var box = new VBoxContainer { Separation = 12, CustomMinimumSize = new Vector2(660, 0) };
        box.AddChild(MakeTitle("Choose a level"));
        var grid = new GridContainer
        {
            Columns = 3,
            HorizontalSeparation = 10,
            VerticalSeparation = 10,
        };
        foreach (var level in Layouts.Levels)
        {
            var index = level.Index;
            var layout = Layouts.GetLayout(level.LayoutId);
            var completed = _progress.CompletedLevels.Contains(index);
            var button = MakeButton($"{(completed ? "★ " : "")}{index}. {level.Name}\n{layout.Name}", () => StartLevel(index), 205, 58);
            grid.AddChild(button);
        }
        box.AddChild(grid);
        box.AddChild(MakeButton("Back", ShowMainMenu, 160, 42));
        ShowOverlay(box);
    }

    public void ShowSettings()
    {
        _settingsReturn = _mode;
        _mode = ScreenMode.Settings;
        var box = new VBoxContainer { Separation = 10, CustomMinimumSize = new Vector2(460, 0) };
        box.AddChild(MakeTitle("Settings"));
        var sound = new CheckBox { Text = "Sound effects", Checked = _settings.SoundEnabled, FontSize = 20, CustomMinimumSize = new Vector2(0, 40) };
        sound.Toggled += (_, value) => { _settings.SoundEnabled = value; _store.SaveSettings(_settings); };
        var motion = new CheckBox { Text = "Reduce motion", Checked = _settings.ReducedMotion, FontSize = 20, CustomMinimumSize = new Vector2(0, 40) };
        motion.Toggled += (_, value) => { _settings.ReducedMotion = value; _store.SaveSettings(_settings); };
        var free = new CheckBox { Text = "Highlight playable tiles", Checked = _settings.HighlightFreeTiles, FontSize = 20, CustomMinimumSize = new Vector2(0, 40) };
        free.Toggled += (_, value) => { _settings.HighlightFreeTiles = value; _store.SaveSettings(_settings); };
        box.AddChild(sound);
        box.AddChild(motion);
        box.AddChild(free);
        box.AddChild(MakeButton("Back", Cancel, 160, 42));
        ShowOverlay(box);
    }

    public void ShowHowToPlay()
    {
        _settingsReturn = _mode;
        _mode = ScreenMode.HowToPlay;
        var box = new VBoxContainer { Separation = 10, CustomMinimumSize = new Vector2(560, 0) };
        box.AddChild(MakeTitle("How to play"));
        box.AddChild(MakeParagraph("Match two free tiles with the same face. A tile is free when nothing covers it and at least one long side is open."));
        box.AddChild(MakeParagraph("Tap or click a tile, then its match. You can also drag one tile onto the other. Use Hint when you are stuck, Undo to rewind, and Shuffle to rearrange the remaining faces."));
        box.AddChild(MakeParagraph("Mouse: click. Keyboard: arrows or Tab to move, Enter to select, H hint, S shuffle, U undo, Esc pause. Gamepad: D-pad or left stick, A select, B back, X hint, Y shuffle, LB undo, Start pause."));
        box.AddChild(MakeButton("Back", Cancel, 160, 42));
        ShowOverlay(box);
    }

    private void ShowResult()
    {
        if (_resultShown) return;
        _resultShown = true;
        _mode = ScreenMode.Result;
        _progress.CompletedLevels.Add(_session!.Level.Index);
        _progress.BestScore = Math.Max(_progress.BestScore, _session.Game.Score);
        _store.SaveProgress(_progress);
        _store.ClearSession();

        var box = new VBoxContainer { Separation = 12, CustomMinimumSize = new Vector2(460, 0) };
        box.AddChild(MakeTitle("Board cleared!"));
        box.AddChild(MakeCaption($"{_session.Level.Name} · Score {_session.Game.Score} · Moves {_session.Game.Moves}"));
        if (_session.Level.Index < Layouts.Levels.Count)
            box.AddChild(MakeButton("Next level", () => StartLevel(_session.Level.Index + 1), 360, 48));
        box.AddChild(MakeButton("Replay", Restart, 360, 46));
        box.AddChild(MakeButton("Level select", ShowLevelSelect, 360, 46));
        box.AddChild(MakeButton("Main menu", ShowMainMenu, 360, 42));
        ShowOverlay(box);
    }

    private void ShowDeadlock()
    {
        _mode = ScreenMode.Deadlock;
        var box = new VBoxContainer { Separation = 12, CustomMinimumSize = new Vector2(460, 0) };
        box.AddChild(MakeTitle("No moves left"));
        box.AddChild(MakeParagraph("No two free tiles match. Shuffle the remaining faces, undo the last move, or restart the board."));
        box.AddChild(MakeButton("Shuffle", () => { Resume(); Shuffle(); }, 360, 48));
        box.AddChild(MakeButton("Undo", () => { Resume(); Undo(); }, 360, 46));
        box.AddChild(MakeButton("Restart", Restart, 360, 46));
        box.AddChild(MakeButton("Main menu", ShowMainMenu, 360, 42));
        ShowOverlay(box);
    }

    private VBoxContainer BuildPauseMenu()
    {
        var box = new VBoxContainer { Separation = 12, CustomMinimumSize = new Vector2(420, 0) };
        box.AddChild(MakeTitle("Paused"));
        box.AddChild(MakeButton("Resume", Resume, 360, 48));
        box.AddChild(MakeButton("Restart board", Restart, 360, 46));
        box.AddChild(MakeButton("Settings", ShowSettings, 360, 46));
        box.AddChild(MakeButton("Main menu", ShowMainMenu, 360, 42));
        return box;
    }

    private void ShowOverlay(Control content)
    {
        foreach (var child in _overlayBox.Children.ToList()) _overlayBox.RemoveChild(child);
        _overlayBox.AddChild(content);
        _scrim.Visible = true;
        _overlayCenter.Visible = true;
        _ui.SetFocus(FindFirstButton(content));
    }

    private void HideOverlay()
    {
        _scrim.Visible = false;
        _overlayCenter.Visible = false;
        _ui.SetFocus(null);
    }

    private void BackToMenu()
    {
        SaveSession();
        ShowMainMenu();
    }

    private void SyncHud()
    {
        if (_session is null) return;
        _titleLabel.Text = $"Rok Mahjong · {_session.Level.Name}";
        _scoreLabel.Text = $"Score {_session.Game.Score}";
        _comboLabel.Text = $"×{Math.Max(1, _session.Game.Combo)}";
        _statusLabel.Text = _session.Game.Status switch
        {
            GameStatus.Won => "Board cleared",
            GameStatus.Deadlocked => "No moves — shuffle or undo",
            _ => $"Tiles left {_session.Game.RemainingCount}",
        };
        _undoButton.Enabled = _session.Game.CanUndo;
    }

    private Button MakeButton(string text, Action action, float width, float height)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 20,
            CustomMinimumSize = new Vector2(width, height),
            FocusMode = FocusMode.All,
            AutomationId = $"button.{++_automationCounter}.{Slug(text)}",
        };
        button.Pressed += (_, _) => action();
        return button;
    }

    private Label MakeTitle(string text) => new()
    {
        Text = text,
        FontSize = 34,
        FontColor = new Color(242, 215, 154),
        HorizontalAlignment = HorizontalAlignment.Center,
        CustomMinimumSize = new Vector2(0, 44),
    };

    private Label MakeCaption(string text) => new()
    {
        Text = text,
        FontSize = 18,
        FontColor = new Color(190, 216, 202),
        HorizontalAlignment = HorizontalAlignment.Center,
        CustomMinimumSize = new Vector2(0, 28),
    };

    private Label MakeParagraph(string text) => new()
    {
        Text = text,
        FontSize = 18,
        FontColor = new Color(208, 226, 216),
        Autowrap = true,
        CustomMinimumSize = new Vector2(520, 0),
    };

    private static string Slug(string text)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var character in text.ToLowerInvariant())
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        return builder.ToString().Trim('-');
    }

    private static Button? FindFirstButton(Control control)
    {
        if (control is Button button && button.Visible && button.Enabled) return button;
        foreach (var child in control.Children)
        {
            var found = FindFirstButton(child);
            if (found is not null) return found;
        }
        return null;
    }
}
