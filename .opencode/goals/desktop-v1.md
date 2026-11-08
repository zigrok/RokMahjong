Deliver a playable, tested desktop v1 of Rok Mahjong from docs/plan.md: a responsive mahjong solitaire with three layout families, 12 levels, classic pair matching, hint/undo/shuffle, mouse/touch/keyboard/gamepad input, save/resume, and automated headless gameplay proof.

```outcome
A user can launch the desktop game, play and complete every level through the real UI, resume a saved game, and watch the same scenarios in a real window or from a replay. The M0-M5 and Desktop v1 acceptance checklists in docs/plan.md are fully checked, and tools/goal-gate.sh prints GOAL GATE OK.
```

```verify
- `dotnet run --project src/RokMahjong.Desktop` opens the main menu.
- All 12 levels can be completed through the real board and input router; solver-agent scenarios cover mouse, touch, keyboard and gamepad.
- Every generated deal and shuffle replays to an empty board through the real MahjongGame API.
- Hint, undo, shuffle, deadlock recovery, restart and save/resume work in a real session.
- The headless gameplay suite, UI flow tests and visual gallery pass.
- The release build has 0 warnings and 0 errors.
- `./tools/plan-progress.sh docs/plan.md --phases M0,M1,M2,M3,M4,M5 --strict` and `./tools/plan-progress.sh docs/plan.md --section "Desktop v1 acceptance" --strict` exit 0.
- Evidence is recorded under artifacts/goal/ and summarized in artifacts/goal/desktop-ready.md.
```

```constraints
- RokMahjong.Core stays pure C#: no MonoGame or Forma references.
- One IInputSource per frame feeds both the board and Forma injection; tests never mutate MahjongGame directly to bypass the input path.
- Do not weaken, skip or retry tests to make the goal pass. Do not remove a failing scenario.
- Generated boards and shuffles must remain provably completable through the game API.
- Do not commit directly to third_party/MonoGame or third_party/Forma main/develop. Upstream changes use a feature branch, a PR and green CI; pin only merged commits or pushed feature-branch commits.
- Do not use git submodule update --recursive; initialize only the required nested submodules.
- Preserve mouse, touch, keyboard and gamepad parity, portrait/landscape layouts, and reduced-motion/accessibility behavior.
- Keep desktop v1 scope: no tray mode, daily challenge, extra themes, localization, online accounts or ads.
```

```scope
- docs/plan.md M0-M5, the desktop host, Core, Game, Content, tests, tools and docs.
- Upstream Forma/MonoGame changes only for a concrete blocker, following CONTRIBUTING.md and upstream CI.
- Out of scope: M6 native mobile, tray mode, daily challenge, special tiles, extra themes, localization, online accounts, ads.
```

stop when: A required upstream Forma/MonoGame change cannot pass its CI after three concrete attempts and needs a product decision; a required SDK, tool or native runtime is unavailable and cannot be installed; the plan's desktop acceptance criteria conflict or need a user choice not covered by the defaults; or the next step needs credentials, a physical device, or scope beyond desktop v1.

verify-cmd: ./tools/goal-gate.sh
