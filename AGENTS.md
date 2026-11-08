# AGENTS.md

Compact guide for coding agents working in **Rok Mahjong**. Read
[`docs/plan.md`](docs/plan.md), [`docs/architecture.md`](docs/architecture.md)
and [`docs/testing.md`](docs/testing.md) before changing architecture, gameplay
or tests.

**Current state:** planning, submodules, CI scaffolding and documentation are in
place. Game code starts at milestone M0. The plan is checklist-driven; keep it
current as you work.

---

## Golden rules

1. **MonoGame displays the game; Core owns the game.** `RokMahjong.Core` must
   stay pure C# — no `Microsoft.Xna.Framework`, no Forma, no rendering, no I/O,
   no wall-clock time, no unseeded randomness.
2. **The model is the authority.** Only `MahjongGame` decides free tiles,
   matches, removal, history and score. Views and input may predict, but never
   commit a move.
3. **One input source per frame.** A single `IInputSource` feeds both the board
   and Forma injection. Tests must not call model methods to bypass the real
   input path.
4. **Determinism is a feature.** Seed every random source, pass time in, and use
   `Game.StepFrame(elapsed)` for tests. Same seed + same input transcript ⇒ same
   state hash.
5. **Test the real path.** Gameplay changes need a headless scenario through the
   real board and input router, not just a Core unit test.
6. **Generated boards must be provably completable.** The stored removal order
   must replay through the real `MahjongGame` API. Never weaken this check.
7. **Do not weaken gates to go green.** No skipped tests, no retry-to-green, no
   unchecked plan boxes, no deleted failing scenarios.
8. **Submodule changes go through upstream PRs.** Never commit directly to
   `third_party/MonoGame` or `third_party/Forma` `develop`/`main`.
9. **Update the plan in the same change.** Mark work `- [~]` when started and
   `- [x]` when completed. The plan is the progress record.
10. **Keep desktop v1 scoped.** Tray mode, daily challenge, extra themes,
    localization and native mobile are post-v1 unless the user asks.

---

## Commands

These are the target commands. Build/test targets land with the M0 solution
skeleton; the submodule and plan commands work today.

```bash
# Repository / submodules
make submodules                 # init MonoGame, Forma and XamlX
make submodule-status           # recursive status
make check-submodules           # pins are reachable from remote branches

# Plan / goal
./tools/plan-progress.sh docs/plan.md
./tools/plan-progress.sh docs/plan.md --phases M0,M1,M2,M3,M4,M5 --strict
./tools/plan-progress.sh docs/plan.md --section "Desktop v1 acceptance" --strict

# Build / run (target; M0+)
dotnet build RokMahjong.slnx
dotnet run --project src/RokMahjong.Desktop

# Tests (target; M0+)
make test                       # core, solvability, Forma headless UI
make test-headless              # MonoGame headless gameplay scenarios
make test-all                   # all non-visual suites
make gallery                    # windowed visual baselines
make headless-runtime           # build the fork's desktopheadless runtime
make playtest SCENARIO=solve-pagoda MODE=windowed
```

Headless runs need the dummy drivers in the **process** environment:

```bash
SDL_VIDEODRIVER=dummy SDL_AUDIODRIVER=dummy make test-headless
```

---

## Plan progress and the desktop goal

`docs/plan.md` is the live checklist:

- `- [ ]` open
- `- [~]` in progress
- `- [x]` done

Run `./tools/plan-progress.sh docs/plan.md` for overall and per-milestone
progress. The desktop goal is complete only when the M0–M5 phases and the
**Desktop v1 acceptance** section are fully checked.

The goal prompt for the installed opencode-goal plugin is
[`docs/goal-desktop-v1.md`](docs/goal-desktop-v1.md). Paste the whole block into
the composer and send it; it starts with `/goal`. Optionally run
`/goal budget 120` first for a larger turn budget.

Use `/goal status`, `/goal pause`, `/goal resume`, `/goal clear` to control it.
The prompt’s `verify-cmd` lines re-read the plan checklists through
`tools/plan-progress.sh`; the reply must also include the actual build, test,
headless gameplay and gallery evidence. Do not edit the prompt to make the goal
easier; ask the user if scope must change.

---

## Repository map

| Path | Responsibility |
| --- | --- |
| `src/RokMahjong.Core` | Pure rules, layouts, deals, solver, history, scoring, serialization |
| `src/RokMahjong.Game` | MonoGame + Forma client, board, shell, input, screens, audio, save |
| `src/RokMahjong.Desktop` | Thin desktop executable and platform lifecycle |
| `src/RokMahjong.Content` | MGCB content builder project |
| `tests/RokMahjong.TestHarness` | Scenario DSL, drivers, assertions, replay format |
| `tests/RokMahjong.Core.Tests` | Unit and property tests (xUnit + CsCheck) |
| `tests/RokMahjong.Game.Tests` | Forma headless UI and MonoGame headless gameplay |
| `tests/RokMahjong.RenderTests` | Windowed gallery and visual regression |
| `tools/RokMahjong.TileArtGen` | Generates the tile atlas and metadata |
| `tools/RokMahjong.Playtest` | Runs, watches, records and replays scenarios |
| `third_party/MonoGame` | Pinned MonoGame fork submodule |
| `third_party/Forma` | Pinned Forma submodule (+ XamlX) |

Dependency direction: `Core ← Game ← Desktop`; `Game → Forma → MonoGame`.
`TestHarness` references Core and Game, never the other way around.

---

## C# and project rules

- Target `net10.0`, nullable enabled, implicit usings, file-scoped namespaces,
  4-space indentation.
- Prefer records and `readonly struct` for value data; keep layouts immutable.
- Inject `ISeededRandom` and a clock/time source into Core. Never use
  `Random.Shared`, `DateTime.Now` or `Stopwatch` in rules.
- Use `System.Text.Json` source generation for save data; write atomically and
  version the schema.
- Avoid allocations and LINQ in per-frame board/update paths.
- Async only for I/O; the game loop, Forma tree and model run on the game thread.
- Dispose textures, render targets, fonts, sound effects and `UIContext`.
- No static mutable game state; one `UIContext` per game.
- `RokMahjong.Core` must compile without MonoGame or Forma references.

---

## UI rules (Forma FHTML/FCSS)

- Views are `.fhtml` + `.fcss`; **do not add XAML source**. The converter compiles
  the dialect through Forma XAML at build time.
- Read `third_party/Forma/docs/agent-design-guide.md` before designing a screen.
- Design values live in the root theme: colors, spacing, radius, motion, type.
  Views use classes and `var(--rok-*)`; no raw hex, no one-off pixel spacing, no
  literal `FontSize`.
- Every interactive control gets a stable `AutomationId` and an accessible name.
- Use Forma controls and containers; do not build a parallel widget system.
- Hot reload is Debug-only. An invalid `.fhtml`/`.fcss` must leave the live tree
  intact.
- The board is a C# `DrawingElement`, not a separate renderer. The HUD and
  menus are Forma views over it.
- Loop: edit → `forma-xaml validate` → `forma-xaml format` → look at it
  (preview/gallery) → run gallery checks.

---

## Board rules

- `MahjongBoardView : DrawingElement` draws in Forma’s `DrawingContext`.
- Model coordinates are half-tile units. All pixel math lives in the presenter.
- Painter’s order is `z`, then `y`, then `x`.
- Tiles use a pre-baked 2.5D atlas: one textured draw per tile, vector paths only
  for selection/hint/particles. Do not tessellate tile bodies every frame.
- Hit testing uses the top-face rectangle, topmost-first.
- Model removal is immediate; the view may keep short-lived ghost tiles for
  animation.
- Minimum touch target is 44 logical units on touch devices.
- Rotation recomputes fit only. It never silently re-deals or loses history.

---

## Input rules

- `IInputSource` reads mouse, touch, keyboard and gamepad once per frame.
- `InputRouter` produces one immutable snapshot, updates `UIContext.InputModality`
  and the last-used device, then routes:
  - pointer/touch → `InjectPointer*`,
  - menu navigation → `InjectKey*`,
  - board navigation → `MahjongBoardView` actions.
- Never read devices directly inside a screen or the board.
- Gamepad actions are abstract (`Navigate*`, `Accept`, `Cancel`, `Hint`,
  `Shuffle`, `Undo`, `Pause`), not raw buttons.
- Tests use `ScriptedInputSource`; production uses `DeviceInputSource`.
- Long-press peek and drag-to-match are part of the board gesture model.

---

## Testing rules

The full strategy is in [`docs/testing.md`](docs/testing.md). Non-negotiables:

- Every generated deal/shuffle replays to empty through `MahjongGame`.
- 10,000-seed smoke sweep on the rule-agreement test; larger nightly sweep.
- Headless gameplay scenarios go through the real input path.
- Headless asserts state, never pixels. Use the windowed gallery for pixels.
- One real game per process in headless tests; Forma text caches require a
  non-parallel collection.
- Use `Game.StepFrame`; never sleep or read the wall clock in a test.
- No skipped tests, no retries to hide flakiness, no weakened assertions.
- Every bug fix gets a regression scenario and replay artifact.
- Every new layout/deck gets a solvability suite.
- Every new input mode and viewport gets a scenario.
- Save/load tests compare state hashes.
- `artifacts/` is gitignored; test failure artifacts and goal evidence belong
  there.

---

## Submodule workflow

The dependencies are pinned submodules. See [`CONTRIBUTING.md`](CONTRIBUTING.md).

- Do **not** use `git submodule update --init --recursive`; MonoGame’s native
  submodules are heavy and only needed for native backends.
- Required: `third_party/MonoGame`, `third_party/Forma`, and Forma’s
  `external/XamlX`.
- Changes inside a submodule use a feature branch:

```bash
./scripts/submodule-start.sh forma feat/mobile-safe-area
# edit, validate upstream and game builds, commit
./scripts/submodule-finish.sh forma feat/mobile-safe-area
```

- Upstream CI must be green before merge.
- After merge, pin the merged commit and commit the submodule pointer separately:

```bash
git -C third_party/Forma fetch origin
git -C third_party/Forma checkout <merged-sha>
git add third_party/Forma
git commit -m "chore(deps): bump Forma to <short-sha>"
```

- `./scripts/check-submodules.sh` rejects dirty submodules, default-branch
  commits and unreachable pins. Run it before pushing.
- Never leave the game history pointing at a local-only or unmerged commit.
  A temporary feature-branch pin is allowed only while the upstream PR is open,
  and must be updated before the game PR merges.

---

## Git and commits

- Use Conventional Commits: `feat(scope): ...`, `fix(scope): ...`,
  `chore(scope): ...`, `docs(scope): ...`, `test(scope): ...`.
- Keep changes focused; do not mix a submodule bump with a gameplay change.
- Do not commit `bin/`, `obj/`, `artifacts/`, local saves or logs.
- Do not force-push `main`.
- Before committing: `git diff --check`, build, tests and plan updates.

---

## Definition of done

For any gameplay/UI change:

- [ ] `dotnet build` has 0 warnings and 0 errors.
- [ ] Relevant Core, solvability and headless tests pass.
- [ ] The change is exercised through the real input path.
- [ ] Portrait and landscape were considered.
- [ ] Mouse, touch, keyboard and gamepad were considered where relevant.
- [ ] Save/load still round-trips the state hash.
- [ ] UI changes have `AutomationId`s and accessible names.
- [ ] Gallery changes are reviewed or explicitly explained.
- [ ] The plan checklist is updated in the same commit.
- [ ] `./scripts/check-submodules.sh` passes if dependencies moved.
- [ ] Docs/diagrams are updated if a boundary changed.

For the desktop goal: both plan-progress commands from
`docs/goal-desktop-v1.md` exit 0, and the reply includes the build, test,
headless gameplay and gallery evidence.

---

## Do not

- Put gameplay rules in `RokMahjong.Game` or a Forma control.
- Use MonoGame/Forma types in `RokMahjong.Core`.
- Bypass the input router in tests or gameplay.
- Read wall-clock time or unseeded randomness in Core.
- Weaken, skip, delete or retry a failing test to make progress.
- Uncheck a plan item that was completed, or check an item without evidence.
- Commit directly to a submodule’s `develop`/`main`.
- Use recursive submodules or a second UI/font/input stack.
- Add post-v1 features (tray, daily, themes, localization, mobile) to the
  desktop v1 goal.
- Assert pixels on the headless backend.
- Hand-edit generated tile atlases or build outputs.
