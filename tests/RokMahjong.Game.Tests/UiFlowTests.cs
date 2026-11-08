using Forma;
using Forma.Testing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RokMahjong.Core;
using RokMahjong.Game;

namespace RokMahjong.Game.Tests;

public class UiFlowTests
{
    [Fact]
    public void Menu_play_pause_resume_and_win_flow_is_reachable_headless()
    {
        var temp = Path.Combine(Path.GetTempPath(), "rok-ui-" + Guid.NewGuid().ToString("N"));
        using var face = LoadFace();
        var body = new DynamicUIFont(face, 20);
        var title = new DynamicUIFont(face, 30);
        var tile = new DynamicUIFont(face, 20);
        var ui = new UIContext
        {
            ViewportSize = new Vector2(1280, 800),
            SuppressPolledInput = true,
            Theme = new Theme
            {
                FontFamily = new UIFontFamily(new UIFont[] { body, title, tile }),
                FontSize = 20,
            },
        };
        var input = new HeadlessInput(ui);
        var shell = new GameShell(ui, new SaveStore(temp), body, title, tile);
        shell.ShowMainMenu();
        shell.Arrange(ui.ViewportSize);
        ui.Layout();

        var play = FindButton(shell.Root, "Play");
        Assert.False(string.IsNullOrWhiteSpace(play.AutomationId));
        Assert.Contains("Play", play.AccessibilityName, StringComparison.OrdinalIgnoreCase);
        input.Click(play);
        Assert.Equal(ScreenMode.LevelSelect, shell.Mode);

        input.Click(FindButton(shell.Root, "First Bloom"));
        Assert.Equal(ScreenMode.Playing, shell.Mode);
        Assert.NotNull(shell.Board);

        input.Click(FindButton(shell.Root, "Pause"));
        Assert.Equal(ScreenMode.Paused, shell.Mode);
        input.Click(FindButton(shell.Root, "Resume"));
        Assert.Equal(ScreenMode.Playing, shell.Mode);

        var board = shell.Board!;
        var source = new ScriptedInputSource();
        var router = new GameInputRouter { Board = board };
        router.Update(source, true, false, 1f, TimeSpan.Zero);
        var solution = DealGenerator.Generate(board.Game.Layout, board.Game.Deck, board.Game.Seed).Solution;
        foreach (var (first, second) in solution)
        {
            Click(source, router, board, first);
            Click(source, router, board, second);
        }

        shell.Update(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)), ui.ViewportSize);
        Assert.Equal(ScreenMode.Result, shell.Mode);
        Assert.Contains(1, new SaveStore(temp).LoadProgress().CompletedLevels);

        ui.Dispose();
        try { Directory.Delete(temp, true); } catch { }
    }

    private static UIFontFace LoadFace()
    {
        using var stream = typeof(GameShell).Assembly.GetManifestResourceStream("RokMahjong.Game.Inter_Regular.ttf")
            ?? throw new InvalidOperationException("Font resource missing.");
        return UIFontFace.FromStream(stream);
    }

    private static Button FindButton(Control root, string text)
    {
        if (root is Button button && button.Text.Contains(text, StringComparison.OrdinalIgnoreCase)) return button;
        foreach (var child in root.Children)
        {
            var found = TryFindButton(child, text);
            if (found is not null) return found;
        }
        throw new InvalidOperationException($"Button containing '{text}' not found.\n{Dump(root)}");
    }

    private static string Dump(Control control, int depth = 0)
    {
        var line = new string(' ', depth * 2) + control.GetType().Name;
        if (control is Button button) line += $" text='{button.Text}' visible={button.Visible}";
        if (control is Label label) line += $" text='{label.Text}' visible={label.Visible}";
        var builder = new System.Text.StringBuilder(line);
        foreach (var child in control.Children) builder.AppendLine().Append(Dump(child, depth + 1));
        return builder.ToString();
    }

    private static Button? TryFindButton(Control root, string text)
    {
        if (root is Button button && button.Text.Contains(text, StringComparison.OrdinalIgnoreCase)) return button;
        foreach (var child in root.Children)
        {
            var found = TryFindButton(child, text);
            if (found is not null) return found;
        }
        return null;
    }

    private static void Click(ScriptedInputSource source, GameInputRouter router, BoardController board, int tileId)
    {
        var tile = board.Game.GetTile(tileId) ?? throw new InvalidOperationException();
        var rect = board.Presentation.GetFaceRect(tile);
        var point = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        source.SetPointer(point, false, InputDeviceKind.Mouse);
        router.Update(source, true, false, 1f, TimeSpan.Zero);
        source.SetPointer(point, true, InputDeviceKind.Mouse);
        router.Update(source, true, false, 1f, TimeSpan.Zero);
        source.SetPointer(point, false, InputDeviceKind.Mouse);
        router.Update(source, true, false, 1f, TimeSpan.Zero);
    }
}
