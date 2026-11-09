# Architecture

> **Status:** planned, M0. These diagrams describe the intended module boundaries
> and runtime flow. Update them as implementation lands; a diagram that no longer
> matches the code is worse than no diagram.

This document covers the game’s runtime architecture. The automated-testing
architecture lives in [testing.md](testing.md), and the product plan lives in
[plan.md](plan.md).

---

## 1. System context

```mermaid
flowchart TB
    player([Player]) --> desktop[Desktop host<br/>RokMahjong.Desktop]
    desktop --> game[RokMahjong.Game<br/>shell, board, input, screens]
    game --> core[RokMahjong.Core<br/>rules, deals, solver, history]
    game --> forma[Forma<br/>retained UI]
    forma --> monogame[MonoGame fork<br/>graphics, input, audio]
    game --> content[RokMahjong.Content<br/>fonts, tile atlas, audio]

    tests[Tests and Playtest] --> harness[RokMahjong.TestHarness<br/>scenarios, drivers, assertions]
    harness --> game
    harness --> core
    harness --> formaui[Forma.Testing<br/>headless UI]
    harness --> monogame

    classDef pure fill:#eef7ee,stroke:#3a8a4a,color:#14331a;
    classDef ui fill:#eef2ff,stroke:#4a68c8,color:#171f45;
    classDef host fill:#fff6e6,stroke:#b07a20,color:#3a2604;
    class core pure;
    class forma,formaui ui;
    class desktop,monogame host;
```

**Rules of the dependency graph**

- `RokMahjong.Core` is pure C#. It must never reference MonoGame or Forma.
- `RokMahjong.Game` owns presentation, input routing, screens and platform
  services. It may reference Core and Forma.
- `RokMahjong.Desktop` is a thin host. It owns the window, command-line
  arguments and platform-specific lifecycle.
- `RokMahjong.Content` produces runtime assets. Game code consumes them through
  a small asset facade, not through content-project details.
- Tests and the `Playtest` tool drive the real game. They do not fork the rules
  or create a second input path.

---

## 2. Module dependencies

```mermaid
flowchart LR
    core[RokMahjong.Core]
    game[RokMahjong.Game]
    desktop[RokMahjong.Desktop]
    content[RokMahjong.Content]
    forma[Forma]
    monogame[MonoGame fork]
    harness[RokMahjong.TestHarness]
    coreTests[RokMahjong.Core.Tests]
    gameTests[RokMahjong.Game.Tests]
    renderTests[RokMahjong.RenderTests]
    playtest[RokMahjong.Playtest]

    game --> core
    game --> forma
    forma --> monogame
    desktop --> game
    content -. assets .-> game

    harness --> core
    harness --> game
    harness --> forma
    harness --> monogame
    coreTests --> core
    gameTests --> harness
    renderTests --> harness
    playtest --> harness
```

| Project | May reference | Must not reference |
| --- | --- | --- |
| `RokMahjong.Core` | BCL only | MonoGame, Forma, Game, Content |
| `RokMahjong.Game` | Core, Forma, MonoGame | Desktop host, test projects |
| `RokMahjong.Desktop` | Game | Core rules directly |
| `RokMahjong.Content` | MGCB tooling | Game runtime types |
| `RokMahjong.TestHarness` | Core, Game, Forma.Testing | Test framework assumptions in production types |
| Test projects | TestHarness, xUnit, CsCheck | Production host internals |

`RokMahjong.TestHarness` is a production-quality library because the same
scenario model powers xUnit tests, the Playtest CLI, replays and the gallery.
It is not a test-only copy of the rules.

### Core domain model

```mermaid
classDiagram
    class MahjongGame {
        +BoardLayout Layout
        +IReadOnlyList~Tile~ Tiles
        +GameStatus Status
        +Selection Selection
        +TrySelect(Tile) bool
        +TryMatch(Tile, Tile) bool
        +Undo() bool
        +Shuffle() ShuffleResult
        +Hint() HintResult
        +Snapshot() GameSnapshot
    }

    class Tile {
        +int Id
        +TileCoord Coord
        +TileFace Face
        +bool Removed
        +MatchGroup Group
    }

    class TileCoord {
        +int X
        +int Y
        +int Z
    }

    class TileFace {
        +MatchGroup Group
        +int Rank
        +string ArtId
    }

    class BoardLayout {
        +string Id
        +string Family
        +IReadOnlyList~TileCoord~ Slots
        +Validate()
    }

    class DealGenerator {
        +Generate(BoardLayout, DeckProfile, ISeededRandom) DealResult
        +Verify(DealResult)
    }

    class ShuffleService {
        +Shuffle(GameSnapshot, ISeededRandom) ShuffleResult
        +VerifyShuffle(GameSnapshot, ShuffleResult)
    }

    class HistoryStack {
        +Push(Move)
        +Undo() Move
        +Clear()
    }

    class ScoreTracker {
        +RegisterMatch()
        +RegisterInvalid()
        +Score
        +Combo
    }

    class HintService {
        +FindHint(GameSnapshot) HintResult
    }

    class BoardSolver {
        +Solve(GameSnapshot, budget) SolverResult
    }

    class GameSession {
        +MahjongGame Game
        +HistoryStack History
        +ScoreTracker Score
        +SaveSnapshot()
        +LoadSnapshot()
    }

    MahjongGame --> BoardLayout
    MahjongGame *-- Tile
    Tile --> TileCoord
    Tile --> TileFace
    GameSession *-- MahjongGame
    GameSession *-- HistoryStack
    GameSession *-- ScoreTracker
    DealGenerator ..> MahjongGame : creates board
    ShuffleService ..> GameSession : rewrites remaining faces
    HintService ..> MahjongGame : reads free tiles
    BoardSolver ..> MahjongGame : oracle
```

The model is deliberately small and serializable. `MahjongGame` owns tile state
and rule decisions; `GameSession` adds presentation-independent progress
(history, score, save snapshot). The solver and hint service read the same state
the game uses; neither has a private copy of the free rule.

---

## 3. Runtime frame

Production and tests share one game object. The only difference is the frame
driver: the normal `Game.Run()` loop in production, and `Game.StepFrame(elapsed)`
in deterministic tests and Playtest runs.

```mermaid
sequenceDiagram
    participant Host as Desktop host / test
    participant Game as RokMahjongGame
    participant Input as InputRouter
    participant UI as Forma UIContext
    participant Shell as GameShell
    participant Board as MahjongBoardView
    participant Session as MahjongGame

    Host->>Game: Run() or StepFrame(elapsed)
    Game->>Input: BeginFrame()
    Input->>UI: InjectPointer* / InjectKey*
    Game->>UI: Update(gameTime)
    UI->>Shell: Process(gameTime)
    Shell->>Board: Update animations and cursor
    Board->>Session: apply pending actions
    Game->>Game: Draw background
    Game->>UI: Draw(GraphicsDevice)
    UI->>Board: Draw(DrawingContext)
    UI->>Shell: Draw HUD and overlays
```

**Frame ordering rules**

1. Read every device once into a single immutable `InputSnapshot`.
2. Inject the same snapshot into Forma that the board and shell read. There is
   no second `Mouse.GetState()`/`Keyboard.GetState()` inside a screen.
3. Advance Forma layout and animations with the same `GameTime` as the game.
4. Apply model changes before drawing; visuals may lag behind the model as
   ghosts/animations, but the model is the source of truth.
5. Draw the background, then the Forma tree (board + HUD + overlays).
6. Never read the wall clock in Core. Time is always passed in.

---

## 4. Input pipeline

```mermaid
flowchart LR
    mouse[Mouse] --> source[IInputSource]
    touch[Touch] --> source
    keyboard[Keyboard] --> source
    gamepad[Gamepad] --> source
    source --> snapshot[InputSnapshot<br/>edges, position, modality]
    snapshot --> router[InputRouter]

    router -->|pointer and touch| inject[Forma InjectPointer*]
    router -->|menu navigation| keys[Forma InjectKey*]
    router -->|board navigation| board[MahjongBoardView]
    router -->|global shortcuts| shell[GameShell]

    inject --> ui[UIContext]
    keys --> ui
    ui --> buttons[HUD and menus]
    board --> session[MahjongGame]
    shell --> session
    session --> events[Game events]
    events --> board
    events --> hud[HUD presenters]
```

- `DeviceInputSource` is the production implementation.
- `ScriptedInputSource` is used by headless tests and Playtest. It is the only
  way a test provides input; tests never call model methods to bypass the board.
- The router tracks the last device and updates `UIContext.InputModality` so
  prompts and adaptive styles match mouse, touch, keyboard or gamepad.
- Touch, mouse and gamepad cursor input all resolve to the same board actions
  (`FocusTile`, `Select`, `Match`, `Peek`, `Cancel`).

---

## 5. Application shell and screen stack

```mermaid
stateDiagram-v2
    [*] --> MainMenu
    MainMenu --> LevelSelect: Play
    MainMenu --> Settings: Settings
    MainMenu --> HowToPlay: How to Play
    LevelSelect --> Playing: Start level
    Playing --> Paused: Pause / Start
    Paused --> Playing: Resume
    Paused --> Settings: Settings
    Paused --> MainMenu: Quit
    Playing --> Result: Board cleared
    Playing --> Deadlock: No legal pairs
    Deadlock --> Playing: Shuffle or Undo
    Result --> LevelSelect: Next / Replay
    Result --> MainMenu: Menu
```

- `GameShell` owns the screen stack and a single active gameplay session.
- Settings and How to Play can be pushed from the menu or paused gameplay, so
  cancel returns to the correct place.
- Modal screens own focus and input until dismissed.
- The board is paused but still rendered while a modal is open.

---

## 6. Board presentation pipeline

```mermaid
flowchart TB
    layout[BoardLayout<br/>slots, faces, depth] --> geometry[LayoutGeometry<br/>world bounds and overlap]
    viewport[Viewport, safe area, breakpoint] --> selector[LayoutSelector<br/>new-game choice]
    selector --> geometry
    geometry --> fit[BoardFit<br/>tile width, origin, layer offset]
    fit --> rects[Tile screen rectangles]
    rects --> draw[BoardView.Draw]
    rects --> hit[BoardView hit testing]
    resize[Resize / orientation] --> fit
    fit --> min{Below minimum touch size?}
    min -->|yes| prompt[Rotate or restart prompt]
    min -->|no| draw
    prompt --> draw
```

- The model uses half-tile units. The presenter owns all pixel math.
- Tile art is an atlas. Each tile is one textured draw plus vector overlays for
  selection, hints and particles.
- Painter’s order is `z`, then `y`, then `x`.
- Rotation keeps the logical board and history. It only recomputes fit; it never
  silently re-deals.

---

## 7. Move flow

```mermaid
sequenceDiagram
    participant Player
    participant Board as MahjongBoardView
    participant Session as MahjongGame
    participant Rule as FreeRule
    participant History as HistoryStack
    participant Events as Game events
    participant Anim as Board animations
    participant UI as HUD / dialogs

    Player->>Board: tap, drag, key or gamepad action
    Board->>Session: TrySelect / TryMatch
    Session->>Rule: IsFree + Matches
    alt valid pair
        Session->>History: push MatchMove
        Session->>Session: remove tiles, update score/combo
        Session->>Events: TilesMatched
        Events->>Anim: match animation
        Events->>UI: score/combo update
        Session->>Session: CheckWin then CheckDeadlock
        Session-->>UI: Result or deadlock event
    else blocked or wrong pair
        Session->>Events: InvalidMove(reason)
        Events->>Anim: shake/flash
        Events->>UI: accessible feedback
    end
```

Model removal happens immediately. The view keeps short-lived ghost tiles for
the removal animation, then drops them.

---

## 8. Save model

```mermaid
flowchart LR
    settings[Settings<br/>audio, theme, input, a11y] --> store[SaveStore]
    progress[Progress<br/>levels, stars, best scores] --> store
    session[ActiveSession<br/>layout, seed, tiles, history, score] --> store
    store --> file[(JSON + schema version + backup)]
    file --> store
    store --> shell[GameShell]
```

- Save writes are atomic (temp file + rename) and versioned.
- The active session contains everything needed to resume exactly: layout id,
  deck profile, seed, tile coordinates/faces/removed flags, selection, history
  and score.
- Tests use an isolated temporary save directory.
- Settings and progress are separate from the active session so a corrupt
  session cannot destroy progress.

---

## 9. Asset pipeline

```mermaid
flowchart LR
    artTool[RokMahjong.TileArtGen<br/>SkiaSharp] --> reference[tile face references<br/>body-less PNGs]
    reference --> pipeline[roktiles CLI<br/>ComfyUI / Layered / OpenRouter]
    pipeline --> history[(artifacts history<br/>PNG + SQLite)]
    pipeline --> glyphs[glyph RGBA + height<br/>assets/art/glyphs]
    pipeline --> legacy[retained painted faces<br/>assets/art/faces]
    glyphs --> artTool
    legacy --> artTool
    artTool --> extrude[axis-aligned extrusion<br/>face + right/bottom side]
    extrude --> atlasPng[tile-atlas.png]
    artTool --> atlasJson[tile-atlas.json<br/>cell/face/depth metadata]
    atlasPng --> mgcb[RokMahjong.Content MGCB]
    atlasJson --> mgcb
    fonts[OFL fonts] --> mgcb
    audio[CC0 audio] --> mgcb
    mgcb --> xnb[Content output]
    xnb --> assets[Game asset facade]
    assets --> board[BoardView]
    assets --> ui[Forma theme/fonts]
    assets --> audioMgr[AudioManager]
```

- The atlas generator emits body-less reference art for all 42 faces, then
  renders a 3D rounded-box tile mesh per face, textured with the selected RGBA
  glyph and its height map.
- `roktiles` owns generation history (gitignored) and selection state; only the
  selected faces and their manifest are committed.
- Generated art and metadata are checked in so runtime builds do not need the
  art tool or ComfyUI.
- Headless tests use placeholder assets and do not require the full Content
  build. They assert on state, never pixels.
- Windowed render tests use the real Content output and a real graphics backend.

---

## 10. Threading and ownership

- The game, Forma tree, input router, board and save store all run on the game
  thread.
- No background thread mutates the model. Content/asset preparation and Debug
  hot reload may do work off-thread, but commit at a frame boundary.
- One `UIContext` owns one root tree. Screens are attached/detached through the
  shell, not by mutating roots from arbitrary code.
- `MahjongGame` owns the rules; the board view owns transient visual state;
  `GameShell` owns navigation; `FormaUi` owns the context and font resources.

---

## 11. Architecture invariants

These are enforced by tests and Debug assertions:

1. **Core purity:** `RokMahjong.Core` never references MonoGame or Forma.
2. **One input source:** every screen and the board read the same frame snapshot.
3. **Determinism:** a seed plus an input transcript produces the same state hash.
4. **Rules authority:** only `MahjongGame` decides free tiles, matches and
   removal; the view may predict but never commits a move.
5. **Monotonic freedom:** removing a tile can never reduce the set of free tiles.
6. **Counts:** every match group always has an even number of remaining faces.
7. **History:** every removed tile is recorded and undo restores the exact state.
8. **Solvability:** every generated or shuffled board carries a verified removal
   order that replays through the real game API.
9. **No UI in saves:** serialized state contains no controls, textures or
   platform objects.
10. **Headless honesty:** headless tests assert state and draw-path execution,
    never pixels.
