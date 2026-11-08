# Rok Mahjong

A relaxing mahjong solitaire matching game built with the
[`zigrok/MonoGame`](https://github.com/zigrok/MonoGame) fork and the
[`zigrok/Forma`](https://github.com/zigrok/Forma) UI toolkit.

The game uses a 2D presentation with 2.5D tiles, supports portrait and landscape
layouts, and is designed for mouse, touch, keyboard and controller from the
start. Desktop is the first target; native mobile is a later Forma workstream.

**Status:** planning approved. See [`docs/plan.md`](docs/plan.md). Implementation
starts at milestone M0.

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
git clone https://github.com/zigrok/rok-mahjong.git
cd rok-mahjong

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
| `tests/` | Core, headless UI/gameplay and render tests |
| `tools/` | Tile art generator and render gallery |
| `docs/plan.md` | Full implementation plan |
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

## Working on MonoGame or Forma

Changes to either dependency are developed on a **feature branch inside the
submodule**, validated by the upstream repository's CI, merged through a pull
request, and then pinned in this repository with a separate submodule-pointer
commit.

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for the full workflow and helper
scripts.

## Documentation

- [`docs/plan.md`](docs/plan.md) — product, architecture, milestones, risks
- [`CONTRIBUTING.md`](CONTRIBUTING.md) — submodule branches, PRs, CI gates
- [`docs/architecture.md`](docs/architecture.md) — implementation notes (M0+)
- [`docs/testing.md`](docs/testing.md) — test and gallery strategy (M0+)
