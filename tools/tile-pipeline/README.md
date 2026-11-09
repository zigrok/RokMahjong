# Tile generation pipeline

Local-first generation of Rok Mahjong tile face art. Every run is kept, any
generation can be selected, and selecting one updates the art the game ships
with.

```text
assets/art/reference/*.png     body-less vector reference art (committed)
        │
        ▼
roktiles generate              ComfyUI edit  → opaque painted tile
                               layered       → transparent RGBA glyph
                               openrouter    → hosted image model
        │
        ▼
artifacts/tile-pipeline/       history: PNGs + SQLite index (gitignored)
        │
        ▼
roktiles select <generation>   RGBA + height → assets/art/glyphs/
                               opaque face   → assets/art/faces/ (retained)
        │
        ▼
make tiles                     3D raymarched tile atlas
```

Tiles are real 3D meshes. `TileRenderer` (in `RokMahjong.TileArtGen`)
raymarches a rounded-box tile with perspective, key/fill lighting, SDF ambient
occlusion and a soft shadow. The pipeline only supplies the top-face texture,
so the mesh, bevel and body stay consistent across every face.

Selected art is cropped to its tile bounds (opaque faces) or kept as a
transparent layer (glyphs) before it is committed.

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
| `ROKTILES_LORA` | empty (no LoRA) |
| `ROKTILES_LAYERED_UNET` | `qwen_image_layered_fp8mixed.safetensors` |
| `ROKTILES_LAYERED_VAE` | `qwen_image_layered_vae.safetensors` |
| `ROKTILES_TIMEOUT` | `1800` seconds |

The default graph is the official *Image Edit (Qwen 2511)* blueprint: GGUF UNet
loader, Qwen 2.5-VL text encoder, AuraFlow sampling shift, CFGNorm, reference
latents, and an `euler`/`simple` sampler at CFG 4.0 (10 steps by default, 20 for
maximum fidelity).

## Transparent glyphs (Qwen-Image-Layered)

`--provider layered` runs the official *Image to Layers* graph. It takes the
same body-less reference, splits it into two RGBA layers, and keeps the
foreground glyph layer (the fully opaque background layer is discarded). The
result is a transparent glyph that the 3D tile can wear directly.

```bash
uv run roktiles generate --face C1 --provider layered --style porcelain
```

Models live in `models/diffusion_models/qwen_image_layered_fp8mixed.safetensors`
and `models/vae/qwen_image_layered_vae.safetensors` (Comfy-Org). The provider is
the only one that returns real alpha; the 2511 edit model decodes RGB.

Selecting a transparent generation writes:

- `assets/art/glyphs/<label>.png` — the RGBA glyph, 512×704.
- `assets/art/glyphs/<label>.height.png` — a soft height field derived from the
  glyph alpha. Drop in a hand-authored height map with the same name to
  override it.

Selecting an opaque generation keeps the older path and writes
`assets/art/faces/<label>.png` instead, so previously generated faces stay
usable.

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
| `roktiles generate …` | generate one face, a group, or all 42 (`--provider layered` for RGBA) |
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

### Fast batch mode (Lightning LoRA)

The 4-step distilled LoRA cuts a face from roughly 20 minutes to a few minutes
with a small quality trade-off. Download it into `models/loras`:

```bash
huggingface-cli download lightx2v/Qwen-Image-Edit-2511-Lightning \
  Qwen-Image-Edit-2511-Lightning-4steps-V1.0-bf16.safetensors \
  --local-dir /Users/zignd/ComfyUI-Shared/models/loras
```

Then generate with 4 steps at CFG 1.0:

```bash
uv run roktiles generate --all --style porcelain --steps 4 --cfg 1.0 \
  --lora Qwen-Image-Edit-2511-Lightning-4steps-V1.0-bf16.safetensors
```

Set `ROKTILES_LORA` to make the LoRA the default, and `--lora-strength` to
tune it (1.0 is the published setting).

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
