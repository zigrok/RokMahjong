# Tile generation pipeline

Local-first generation of Rok Mahjong tile face art. Every run is kept, any
generation can be selected, and selecting one updates the art the game ships
with.

```text
assets/art/reference/*.png     body-less vector reference art (committed)
        │
        ▼
roktiles generate              ComfyUI (Qwen-Image-Edit 2511) or OpenRouter
        │
        ▼
artifacts/tile-pipeline/       history: PNGs + SQLite index (gitignored)
        │
        ▼
roktiles select <generation>   pick one, copy into assets/art/faces/
        │
        ▼
make tiles                     rebuild assets/art/tile-atlas.png
```

Generated art fills the tile's top face, including the frame the model paints.
The vector 2.5D side, bevel and shadow are still drawn behind it, and faces
without a selection fall back to the vector symbol.

Selected art is cropped to its tile bounds before it is committed, so
the model's frame lines up with the atlas cell instead of floating in a
background margin.

## Requirements

- Python 3.11+ and [`uv`](https://docs.astral.sh/uv/)
- .NET 10 SDK (for the atlas packer)
- ComfyUI with the Qwen-Image-Edit 2511 GGUF model (local, default)
- Optional: `OPENROUTER_API_KEY` for hosted image models

## Quick start

```bash
cd tools/tile-pipeline
uv sync
uv run roktiles doctor            # check ComfyUI, references and dotnet

uv run roktiles generate --face C1 --style porcelain --seed 42
uv run roktiles history --face C1
uv run roktiles sheet --face C1 --open   # compare candidates visually
uv run roktiles select g-1a2b3c4d        # copies the face and rebuilds the atlas
```

Or through `make` from the repository root:

```bash
make tilegen ARGS="doctor"
make tilegen ARGS="generate --face C1 --style porcelain"
make tilegen ARGS="history --face C1"
```

## Starting ComfyUI

The repository ships a shared-model path config at
`tools/tile-pipeline/comfy/extra_model_paths.yaml`. Point it at your ComfyUI
shared model directory, then:

```bash
cd /path/to/ComfyUI
.venv/bin/python main.py \
  --listen 127.0.0.1 --port 8188 \
  --extra-model-paths-config /path/to/RokMahjong/tools/tile-pipeline/comfy/extra_model_paths.yaml
```

Models are selected with environment variables:

| Variable | Default |
| --- | --- |
| `COMFYUI_URL` | `http://127.0.0.1:8188` |
| `ROKTILES_UNET` | `qwen-image-edit-2511-Q4_K_M.gguf` |
| `ROKTILES_CLIP` | `qwen_2.5_vl_7b_fp8_scaled.safetensors` |
| `ROKTILES_VAE` | `qwen_image_vae.safetensors` |
| `ROKTILES_TIMEOUT` | `1800` seconds |

The default graph is the official *Image Edit (Qwen 2511)* blueprint: GGUF UNet
loader, Qwen 2.5-VL text encoder, AuraFlow sampling shift, CFGNorm, reference
latents, and an `euler`/`simple` sampler at CFG 4.0 (10 steps by default, 20 for
maximum fidelity).

## Styles

`roktiles styles` lists the presets: `porcelain` (default), `lacquer`, `ink`,
`jade`, `enamel`, `minimal`. Each preset only changes the look; the subject and
layout sentence is built per face, so the glyph or pip layout never drifts.

Add `--extra "…"` to append art direction to a single run.

## Commands

| Command | Purpose |
| --- | --- |
| `roktiles faces` | list all 42 faces and what is selected |
| `roktiles styles` | list style presets |
| `roktiles prompt C1 --style ink` | print the exact prompt and negative |
| `roktiles generate …` | generate one face, a group, or all 42 |
| `roktiles history` | list generations, `*` marks the selection |
| `roktiles sheet --face C1 --open` | contact sheet of candidates |
| `roktiles show <id> --open` | print a generation's path and prompt |
| `roktiles runs` | list generation runs |
| `roktiles select <id>…` | select generations, copy and rebuild the atlas |
| `roktiles select-run <run>` | select every face produced by one run |
| `roktiles apply` | re-apply the current selections and rebuild |
| `roktiles reset --face C1` | drop a face back to vector art |
| `roktiles status` | pipeline overview |
| `roktiles doctor` | prerequisite checks |

`generate` selectors: `--face LABEL` (repeatable), `--group characters|bamboo|
circles|winds|dragons|flowers|seasons`, or `--all`.

Useful flags: `--seed`, `--steps`, `--cfg`, `--denoise`, `--sampler`,
`--scheduler`, `--shift`, `--keep-going`, `--dry-run`, `--provider`,
`--model`.

On Apple Silicon a 10-step CFG 4.0 run takes roughly a minute per step. Use
`--steps 2 --cfg 1.0` to check composition quickly, and `--steps 20` for the
maximum-fidelity blueprint setting.

## Batch generation

Faces are independent, so a full set is one command:

```bash
uv run roktiles generate --all --style porcelain --steps 6 --keep-going
```

That is several hours on Apple Silicon. Run it under `tmux` or `nohup` so it
survives a closed terminal; every completed face is committed to the history
database immediately, and the run prints an ETA after each face. If it is
interrupted, re-run with `--face` for the faces that are missing.

Batch flags that matter: `--keep-going` (do not stop on the first failure),
`--seed` (reproducible per-face seeds), and `--style` (one look per run so the
set stays coherent).

## History and selection

Every generation is written to
`artifacts/tile-pipeline/history/<run>/<face>_<generation>.png` and indexed in
`artifacts/tile-pipeline/history.sqlite3` together with its prompt, negative
prompt, seed, model and parameters. The directory is gitignored: history stays
local and never bloats the repository.

Selecting writes normalized `416×576` face PNGs to `assets/art/faces/` plus
`assets/art/faces/manifest.json` recording the provenance of each selected
face. Both are committed, so the game builds from the selected art without
needing ComfyUI, Python or the history database.

## OpenRouter (optional)

Set `OPENROUTER_API_KEY` (and optionally `OPENROUTER_MODEL`,
`OPENROUTER_BASE_URL`) and use `--provider openrouter`. The adapter sends the
reference image plus prompt to an image-capable chat model and stores the
returned image in the same history. Without a key the adapter reports that it
is disabled and local ComfyUI remains the default.

## Why Qwen-Image-Edit

SDXL Turbo img2img at a useful denoise already corrupted Chinese glyphs during
testing. Qwen-Image-Edit 2511 is an instruction-driven editor: the body-less
vector reference supplies structure, and the prompt only changes the style, so
characters keep their strokes and pip grids keep their counts.

## Repository notes

- `assets/art/reference/` is regenerated by `make tiles` from the vector code.
  Do not hand-edit it.
- The atlas and metadata in `assets/art/` are generated by
  `tools/RokMahjong.TileArtGen`; never hand-edit them.
- `tools/tile-pipeline/.venv/` and `artifacts/` are gitignored.
