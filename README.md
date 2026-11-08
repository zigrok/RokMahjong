# Rok Mahjong

A relaxing mahjong solitaire matching game built with the
[`zigrok/MonoGame`](https://github.com/zigrok/MonoGame) fork and the
[`zigrok/Forma`](https://github.com/zigrok/Forma) UI toolkit.

The game uses a 2D presentation with 2.5D tiles, supports portrait and landscape
layouts, and is designed for mouse, touch, keyboard and controller from the
start. Desktop is the first target; native mobile is a later Forma workstream.

**Status:** planning approved, including the architecture and testing strategy.
See [`docs/plan.md`](docs/plan.md). Implementation starts at milestone M0.

Gameplay is designed to be tested through the real input and UI path on the
MonoGame fork's headless backend, and the same scenarios can be watched in a
real window or replayed from a failure artifact. See
[`docs/testing.md`](docs/testing.md).

**Initial dependency baseline:** MonoGame `260e930`, Forma `8827393`, XamlX
`0337e9b`. The pinned Forma core and `Forma.Xaml.Build` compile cleanly against
the pinned MonoGame `DesktopGL` project.

## Requirements

- .NET SDK **10.0.100 or newer** (see `global.json`)
- Git 2.40+
- Desktop OpenGL-capable machine for the default `DesktopGL` build
- Optional: [GitHub CLI](https://cli.github.com/) (`gh`) for the submodule PR
  workflow
- Optional: native MonoGame backend toolchains (Metal/Vulkan/DX12) only when
  building those backends

## Checkout

The MonoGame and Forma dependencies are Git submodules. Do **not** use
`--recurse-submodules`: MonoGame contains many native backend submodules that are
only needed for specific builds.

```bash
git clone https://github.com/zigrok/RokMahjong.git
cd RokMahjong

# Required: the game framework and the UI toolkit.
git submodule update --init third_party/MonoGame third_party/Forma

# Required by Forma's compiled FHTML/XAML pipeline.
git -C third_party/Forma submodule update --init external/XamlX
```

Optional runtime SVG icons (only if the bitmap icon atlas is not enough):

```bash
git -C third_party/Forma submodule update --init external/ThorVG
```

Native MonoGame backends initialize their own submodules on demand. For example,
the native Vulkan/Metal path needs SDL3, FAudio and SPIRV-Cross; see
`third_party/MonoGame/README.md` and `BACKEND-DEPENDENCIES.md` before building
those targets. The default `DesktopGL` build does not need them.

To inspect the exact dependency revisions:

```bash
git submodule status --recursive
```

## Repository map

| Path | Purpose |
| --- | --- |
| `src/RokMahjong.Core` | Pure C# rules, layouts, deals, history, scoring |
| `src/RokMahjong.Game` | MonoGame + Forma client, board and screens |
| `src/RokMahjong.Desktop` | Desktop executable host |
| `src/RokMahjong.Content` | MGCB content builder project |
| `tests/` | Test harness, core/solvability, headless gameplay and render tests |
| `tools/` | Tile art generator, Playtest runner, progress/gate scripts and gallery |
| `docs/` | Plan, architecture diagrams and testing strategy |
| `.opencode/` | Project `/go` command and desktop goal contract |
| `AGENTS.md` | Project rules and definition of done for coding agents |
| `third_party/` | Pinned MonoGame and Forma submodules |

The project skeleton lands with milestone M0. Until then, only the repository
scaffolding, submodules and documentation exist.

## Development

```bash
# Initialize/update the required submodules.
make submodules

# Show submodule state.
make submodule-status

# Verify every pinned submodule commit is reachable from a remote branch.
make check-submodules
```

Build and test targets will be added alongside the M0 solution skeleton.

## Plan and goal tracking

`docs/plan.md` is a live checklist (`- [ ]` open, `- [~]` in progress,
`- [x]` done).

```bash
make progress           # overall progress
make progress-desktop   # M0-M5 desktop progress
make goal-gate          # re-read the desktop goal evidence
```

The project `/go` command reads `.opencode/goals/desktop-v1.md`, sets a
120-turn goal budget, and delegates to the opencode-goal plugin’s `/goal`
command. It is a small local plugin rather than a Markdown prompt template
because OpenCode prompt templates cannot drive the standing goal loop. The goal
is complete only when the plan’s M0–M5 phases and the **Desktop v1 acceptance**
checklist are fully checked and `make goal-gate` prints `GOAL GATE OK`.

## Testing

The test strategy is layered:

- **Core and solvability:** pure C# tests prove that every generated deal and
  shuffle carries a verified removal order that replays through the real game
  API.
- **Headless UI and gameplay:** Forma’s headless UI driver plus the MonoGame
  fork’s headless backend run the real game loop, input router and board with
  deterministic `Game.StepFrame` stepping.
- **Visual and soak:** a real graphics backend renders deterministic gallery
  scenes and scenario recordings; nightly runs cover large seed sweeps, long
  sessions and performance.

Planned commands:

```bash
make headless-runtime  # build the fork's desktopheadless library
make test              # core + solvability + headless UI
make test-headless     # full gameplay scenarios on the headless backend
make gallery           # windowed visual baselines
make playtest SCENARIO=solve-pagoda MODE=windowed
```

`RokMahjong.Playtest` runs the same scenario headless for assertions, windowed
for observation, or from a recorded replay. Failures produce a replay, state
dump and UI tree so they can be reproduced locally.

Full details: [`docs/testing.md`](docs/testing.md).

## Working on MonoGame or Forma

Changes to either dependency are developed on a **feature branch inside the
submodule**, validated by the upstream repository's CI, merged through a pull
request, and then pinned in this repository with a separate submodule-pointer
commit.

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for the full workflow and helper
scripts.

## Documentation

- [`docs/plan.md`](docs/plan.md) — product, architecture, milestones, risks and live checklist
- [`docs/architecture.md`](docs/architecture.md) — Mermaid architecture diagrams and invariants
- [`docs/testing.md`](docs/testing.md) — test layers, solvability, headless gameplay, replays, CI gates
- [`AGENTS.md`](AGENTS.md) — project rules and definition of done for coding agents
- [`CONTRIBUTING.md`](CONTRIBUTING.md) — submodule branches, PRs, CI gates
- [`.opencode/goals/desktop-v1.md`](.opencode/goals/desktop-v1.md) — `/go` desktop goal contract
