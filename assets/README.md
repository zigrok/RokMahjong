# Assets

## Tile art

All of `art/` is generated. Do not hand-edit any of it.

| Path | What it is |
| --- | --- |
| `art/tile-atlas.png` | 42-face atlas with the vector 2.5D tile body baked in |
| `art/tile-atlas.json` | atlas cell/face metadata and face order |
| `art/reference/*.png` | body-less vector face art used as generation references |
| `art/glyphs/*.png` | selected RGBA glyphs (transparent, 512×704) |
| `art/glyphs/*.height.png` | derived height fields for the 3D face relief |
| `art/glyphs/manifest.json` | provenance for each selected glyph |
| `art/faces/*.png` | retained painted 2D faces (legacy fallback) |
| `art/faces/manifest.json` | provenance for each retained face |

### Rebuild

```bash
make tiles
```

`tools/RokMahjong.TileArtGen` draws each cell as an axis-aligned 2.5D tile: a
flat rounded-rectangle top face plus a shaded right/bottom side inside the tile
footprint. It uses
`art/glyphs/<label>.png` (RGBA) and `art/glyphs/<label>.height.png` when
present, then a retained `art/faces/<label>.png`, then the vector symbol.

### Generate and choose face art

```bash
make tilegen ARGS="generate --face C1 --style porcelain"
make tilegen ARGS="history --face C1"
make tilegen ARGS="select <generation-id>"
make tilegen ARGS="reset --face C1"
```

Generation history lives in `artifacts/tile-pipeline/` and is gitignored. Only
the selected faces and `art/faces/manifest.json` are committed. See
[`../tools/tile-pipeline/README.md`](../tools/tile-pipeline/README.md).

## Fonts

`fonts/NotoSerifCJKsc-Subset.otf` is a 14 KB subset of Noto Serif CJK SC
(SIL Open Font License 1.1) containing only the glyphs used by the reference
art. The license is in `fonts/LICENSE.NotoSerifCJKsc.txt`. The subset is used
only by the build-time generator; it is not shipped with the game.
