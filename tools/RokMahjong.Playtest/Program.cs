using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RokMahjong.Core;
using RokMahjong.Game;

var levelIndex = 1;
var device = InputDeviceKind.Mouse;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--level" && i + 1 < args.Length) levelIndex = int.Parse(args[++i]);
    if (args[i] == "--device" && i + 1 < args.Length) device = Enum.Parse<InputDeviceKind>(args[++i], true);
}

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
        Click(firstId);
        Click(secondId);
    }
    else if (device == InputDeviceKind.Keyboard)
    {
        Focus(firstId, Keys.Tab, null);
        PressKey(Keys.Enter);
        Focus(secondId, Keys.Tab, null);
        PressKey(Keys.Enter);
    }
    else
    {
        Focus(firstId, null, Buttons.RightShoulder);
        PressGamepad(Buttons.A);
        Focus(secondId, null, Buttons.RightShoulder);
        PressGamepad(Buttons.A);
    }
}

var result = new
{
    level = session.Level.Index,
    levelName = session.Level.Name,
    layout = session.Game.Layout.Id,
    deck = session.Game.Deck.Id,
    device = device.ToString(),
    status = session.Game.Status.ToString(),
    remaining = session.Game.RemainingCount,
    moves = session.Game.Moves,
    score = session.Game.Score,
    solutionPairs = solution.Count,
};
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

void Click(int tileId)
{
    var tile = board.Game.GetTile(tileId) ?? throw new InvalidOperationException();
    var rect = board.Presentation.GetFaceRect(tile);
    var point = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    source.SetPointer(point, false, device);
    router.Update(source, true, false, 1f, TimeSpan.Zero);
    source.SetPointer(point, true, device);
    router.Update(source, true, false, 1f, TimeSpan.Zero);
    source.SetPointer(point, false, device);
    router.Update(source, true, false, 1f, TimeSpan.Zero);
}

void Focus(int tileId, Keys? key, Buttons? button)
{
    for (var i = 0; i < board.Game.FreeTiles().Count + 2 && board.FocusTileId != tileId; i++)
    {
        if (key.HasValue) PressKey(key.Value);
        else if (button.HasValue) PressGamepad(button.Value);
    }
    if (board.FocusTileId != tileId) throw new InvalidOperationException($"Could not focus tile {tileId}.");
}

void PressKey(Keys key)
{
    source.PressKey(key);
    router.Update(source, true, false, 1f, TimeSpan.Zero);
    source.ReleaseKey(key);
    router.Update(source, true, false, 1f, TimeSpan.Zero);
}

void PressGamepad(Buttons button)
{
    source.PressGamepadButton(button);
    router.Update(source, true, false, 1f, TimeSpan.Zero);
    source.ReleaseGamepadButton(button);
    router.Update(source, true, false, 1f, TimeSpan.Zero);
}
