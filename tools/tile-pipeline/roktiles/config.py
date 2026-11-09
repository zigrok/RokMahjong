"""Repository paths and environment-driven configuration."""

from __future__ import annotations

import os
from pathlib import Path


def _find_repo_root(start: Path) -> Path:
    for candidate in (start, *start.parents):
        if (candidate / "RokMahjong.slnx").is_file():
            return candidate
    raise RuntimeError(
        "Could not locate the Rok Mahjong repository root (RokMahjong.slnx)."
    )


REPO_ROOT = _find_repo_root(Path(__file__).resolve())
ART_ROOT = REPO_ROOT / "assets" / "art"
REFERENCE_DIR = ART_ROOT / "reference"
GLYPH_DIR = ART_ROOT / "glyphs"
GLYPH_MANIFEST = GLYPH_DIR / "manifest.json"
LEGACY_FACE_DIR = ART_ROOT / "faces"
SELECTED_DIR = LEGACY_FACE_DIR
SELECTED_MANIFEST = SELECTED_DIR / "manifest.json"
ATLAS_DIR = ART_ROOT
GENERATOR_PROJECT = REPO_ROOT / "tools" / "RokMahjong.TileArtGen"

PIPELINE_ROOT = REPO_ROOT / "tools" / "tile-pipeline"
ARTIFACTS = REPO_ROOT / "artifacts" / "tile-pipeline"
HISTORY_DIR = ARTIFACTS / "history"
DB_PATH = ARTIFACTS / "history.sqlite3"

COMFYUI_URL = os.environ.get("COMFYUI_URL", "http://127.0.0.1:8188").rstrip("/")
COMFYUI_UNET = os.environ.get(
    "ROKTILES_UNET", "qwen-image-edit-2511-Q4_K_M.gguf"
)
COMFYUI_CLIP = os.environ.get(
    "ROKTILES_CLIP", "qwen_2.5_vl_7b_fp8_scaled.safetensors"
)
COMFYUI_VAE = os.environ.get("ROKTILES_VAE", "qwen_image_vae.safetensors")
COMFYUI_LORA = os.environ.get("ROKTILES_LORA", "")
COMFYUI_LAYERED_UNET = os.environ.get(
    "ROKTILES_LAYERED_UNET", "qwen-image-layered-Q4_K_M.gguf"
)
COMFYUI_LAYERED_VAE = os.environ.get(
    "ROKTILES_LAYERED_VAE", "qwen_image_layered_vae.safetensors"
)
COMFYUI_TIMEOUT = int(os.environ.get("ROKTILES_TIMEOUT", "1800"))

OPENROUTER_BASE_URL = os.environ.get(
    "OPENROUTER_BASE_URL", "https://openrouter.ai/api/v1"
).rstrip("/")
OPENROUTER_API_KEY = os.environ.get("OPENROUTER_API_KEY")
OPENROUTER_MODEL = os.environ.get(
    "OPENROUTER_MODEL", "google/gemini-2.5-flash-image"
)

SELECTED_FACE_SIZE = (416, 576)
GLYPH_SIZE = (512, 704)


def ensure_directories() -> None:
    for directory in (REFERENCE_DIR, GLYPH_DIR, SELECTED_DIR, HISTORY_DIR):
        directory.mkdir(parents=True, exist_ok=True)
