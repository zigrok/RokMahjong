# Contributing to Rok Mahjong

## Ground rules

- `main` is the game repository's integration branch. Do work on a feature
  branch, open a pull request, and wait for CI before merging.
- `third_party/MonoGame` and `third_party/Forma` are **Git submodules pinned to
  exact commits**. Do not commit changes directly to their `develop` or `main`
  branches, and do not leave the game repository pointing at a local-only commit.
- Keep upstream changes separate from game changes:
  1. upstream feature branch + PR,
  2. upstream CI green + merge,
  3. submodule pointer bump in this repository,
  4. game changes that depend on it.
- One logical change per PR. Do not mix a MonoGame change and a Forma change in
  the same upstream PR.

## Submodule map

| Submodule | Upstream | Default branch |
| --- | --- | --- |
| `third_party/MonoGame` | `zigrok/MonoGame` | `develop` |
| `third_party/Forma` | `zigrok/Forma` | `main` |
| `third_party/Forma/external/XamlX` | `zignd/XamlX` | `forma/nativeaot-compat` (pinned by Forma) |

The nested XamlX revision is owned by Forma. Do not update it independently in a
game PR; change it as part of the Forma change that requires it.

## Branch naming

Use `<type>/<short-slug>`:

- `feat/...` — new capability
- `fix/...` — bug fix
- `chore/...` — build, dependency or tooling work
- `docs/...` — documentation
- `test/...` — tests only
- `ci/...` — CI configuration

Examples: `feat/mobile-safe-area`, `fix/gamepad-focus-order`,
`fix/tile-free-rule`.

## Starting an upstream change

```bash
# Creates the branch from the latest origin/<default> inside the submodule.
./scripts/submodule-start.sh forma feat/mobile-safe-area

# Or:
./scripts/submodule-start.sh monogame fix/headless-present
```

The script:

1. fetches `origin`,
2. creates a feature branch from the default branch,
3. prints the push/PR commands.

It never commits or pushes for you.

### If you use a fork

The helper scripts assume you can push a branch to the upstream repository. If
you cannot, add your fork as a remote inside the submodule and push there:

```bash
cd third_party/Forma
git remote add fork git@github.com:<your-account>/Forma.git
git push -u fork feat/mobile-safe-area
gh pr create --repo zigrok/Forma --base main --head <your-account>:feat/mobile-safe-area
```

## Validating an upstream change

Run the upstream repository's own checks in addition to the game build/tests.

Forma (from `third_party/Forma`):

```bash
dotnet build src/Forma/Forma.csproj -p:FormaRuntime=MonoGame \
  -p:MonoGameProjectPath="$PWD/../MonoGame/MonoGame.Framework/MonoGame.Framework.DesktopGL.csproj"

# Plus the focused test/validation target for the area you changed.
# At minimum, the same checks the upstream PR runs in CI.
```

MonoGame (from `third_party/MonoGame`):

```bash
dotnet build MonoGame.Framework/MonoGame.Framework.DesktopGL.csproj

# Plus the focused framework tests for the area you changed.
```

Game repository:

```bash
make check-submodules
# Build/test targets are added with the M0 solution skeleton.
```

## Opening the upstream PR

```bash
./scripts/submodule-finish.sh forma feat/mobile-safe-area
```

The script:

1. verifies the submodule worktree is clean,
2. pushes the branch to `origin`,
3. opens a PR with `gh` when available (or prints the compare URL),
4. prints the pointer-update commands for after the merge.

Requirements for the PR:

- The branch is pushed before the game branch that pins it.
- The upstream PR description explains the game-facing need.
- Upstream CI is green. Do not merge on a red or skipped required check.
- The commit is reachable from a remote branch (not only your machine).

## Pinning the merged commit

After the upstream PR merges:

```bash
# Forma example. Replace <merged-sha> with the merge/squash commit.
git -C third_party/Forma fetch origin
git -C third_party/Forma checkout <merged-sha>

git add third_party/Forma
git commit -m "chore(deps): bump Forma to <short-sha>"
```

For a deliberate "take the latest upstream" update:

```bash
git submodule update --remote third_party/Forma
git diff --submodule=log third_party/Forma
git add third_party/Forma
git commit -m "chore(deps): update Forma"
```

Review the diff before committing. A submodule bump should be its own commit and
should state why the pin moved.

## The temporary feature-branch pin

While an upstream PR is open, the game branch may pin the feature-branch commit
so the game can be tested end-to-end. This is temporary:

- push the upstream feature branch first;
- `scripts/check-submodules.sh` must pass;
- do **not** merge the game PR until the upstream PR has merged and the pointer
  has been updated to the merged commit.

This avoids a game history that depends on a branch that may later be deleted.

## Pre-push checks

```bash
# Every pinned commit must exist on a remote branch, so CI can fetch it.
./scripts/check-submodules.sh

# The superproject must not have local modifications inside the submodules.
git submodule status

# The game repository itself must be clean.
git status --short
```

`scripts/check-submodules.sh` is also intended to run in game CI. A green local
run does not replace upstream CI, but it prevents the common "CI can't fetch this
commit" failure.

## Pull request checklist

- [ ] Game changes are on a feature branch, not `main`.
- [ ] No direct commits to the submodules' `develop`/`main`.
- [ ] Upstream PR links are included when a submodule changed.
- [ ] Upstream CI is green.
- [ ] Submodule pointers point at merged commits (or a pushed feature branch,
      with the merge blocked until the upstream PR lands).
- [ ] `./scripts/check-submodules.sh` passes.
- [ ] Core tests, headless UI tests and render checks pass.
- [ ] New UI has `AutomationId`s and accessible names.
- [ ] Portrait and landscape layouts were exercised.
- [ ] Mouse, touch, keyboard and controller paths were exercised where relevant.

## CI expectations

Two separate gates apply:

1. **Upstream gate:** the MonoGame/Forma PR passes the upstream repository's
   required CI. This is the gate that protects the shared dependencies.
2. **Game gate:** the game repository builds and tests against the pinned
   submodule commits, and verifies the pins are publicly reachable. The
   `.github/workflows/submodule-pins.yml` workflow runs
   `scripts/check-submodules.sh` on every push and pull request.

Never merge an upstream change just because the game build passes locally. Never
merge a game change whose gitlinks cannot be fetched by CI.
