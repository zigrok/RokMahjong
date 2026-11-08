# Assets

## Tile art

All of `art/` is generated. Do not hand-edit any of it.

| Path | What it is |
| --- | --- |
| `art/tile-atlas.png` | 42-face atlas with the vector 2.5D tile body baked in |
| `art/tile-atlas.json` | atlas cell/face metadata and face order |
| `art/reference/*.png` | body-less vector face art used as generation references |
| `art/faces/*.png` | selected generated face art (416×576) |
| `art/faces/manifest.json` | provenance for each selected face |

### Rebuild

```bash
make tiles
```

`tools/RokMahjong.TileArtGen` draws the atlas and rewrites `art/reference/`.
When `art/faces/<label>.png` exists it is drawn as the tile top face over the
vector side and shadow; otherwise the vector symbol is used. Selected art is
cropped to its tile bounds before it is written here.

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
