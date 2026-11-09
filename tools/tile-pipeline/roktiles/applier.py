"""Applying selected generations to the committed game assets."""

from __future__ import annotations

import io
import json
import shutil
import subprocess
from pathlib import Path

from PIL import Image, ImageChops

from . import config, glyphs, store


def _load_manifest(path: Path) -> dict[str, dict]:
    if not path.is_file():
        return {}
    try:
        return json.loads(path.read_text())
    except json.JSONDecodeError:
        return {}


def _save_manifest(path: Path, manifest: dict[str, dict]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(dict(sorted(manifest.items())), indent=2) + "\n")


def _background_colour(image: Image.Image) -> tuple[int, int, int]:
    width, height = image.size
    pad = max(8, min(width, height) // 40)
    boxes = [
        (0, 0, pad, pad),
        (width - pad, 0, width, pad),
        (0, height - pad, pad, height),
        (width - pad, height - pad, width, height),
    ]
    samples = [
        image.crop(box).resize((1, 1), Image.BOX).getpixel((0, 0)) for box in boxes
    ]
    channels = []
    for index in range(3):
        values = sorted(sample[index] for sample in samples)
        channels.append(values[len(values) // 2])
    return tuple(channels)  # type: ignore[return-value]


def crop_to_artwork(image: Image.Image, inset: float = 0.015) -> Image.Image:
    """Crop away the flat background margin around a legacy painted tile."""
    rgb = image.convert("RGB")
    width, height = rgb.size
    background = _background_colour(rgb)
    difference = ImageChops.difference(
        rgb, Image.new("RGB", rgb.size, background)
    ).convert("L")
    mask = difference.point(lambda value: 255 if value > 18 else 0)
    bounds = mask.getbbox()
    if bounds is None:
        return rgb

    left, top, right, bottom = bounds
    box_width, box_height = right - left, bottom - top
    if box_width > width * 0.92 and box_height > height * 0.92:
        return rgb

    dx = int(box_width * inset)
    dy = int(box_height * inset)
    if box_width - 2 * dx < 16 or box_height - 2 * dy < 16:
        return rgb
    return rgb.crop((left + dx, top + dy, right - dx, bottom - dy))


def _fit_canvas(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    copy = image.copy()
    copy.thumbnail(size, Image.LANCZOS)
    canvas = Image.new("RGBA", size, (0, 0, 0, 0))
    canvas.alpha_composite(copy, ((size[0] - copy.width) // 2, (size[1] - copy.height) // 2))
    return canvas


def _apply_glyph(label: str, generation: store.Generation, image: Image.Image) -> None:
    canvas = _fit_canvas(image.convert("RGBA"), config.GLYPH_SIZE)
    glyph_path = config.GLYPH_DIR / f"{label}.png"
    height_path = config.GLYPH_DIR / f"{label}.height.png"
    canvas.save(glyph_path, format="PNG", optimize=True)
    glyphs.derive_height(canvas).save(height_path, format="PNG", optimize=True)

    # A glyph supersedes any legacy painted face for the same label.
    (config.LEGACY_FACE_DIR / f"{label}.png").unlink(missing_ok=True)
    legacy = _load_manifest(config.SELECTED_MANIFEST)
    if label in legacy:
        legacy.pop(label)
        _save_manifest(config.SELECTED_MANIFEST, legacy)

    manifest = _load_manifest(config.GLYPH_MANIFEST)
    manifest[label] = {
        "kind": "glyph",
        "generation": generation.id,
        "run": generation.run_id,
        "provider": generation.params.get("provider", ""),
        "model": generation.params.get("model", ""),
        "style": generation.params.get("style", ""),
        "seed": generation.seed,
        "prompt": generation.prompt,
        "layer": generation.params.get("glyph_layer"),
        "width": canvas.width,
        "height": canvas.height,
        "heightMap": str(height_path.relative_to(config.REPO_ROOT)),
        "createdAt": generation.created_at,
        "source": str(generation.image_path.relative_to(config.REPO_ROOT)),
    }
    _save_manifest(config.GLYPH_MANIFEST, manifest)


def _apply_legacy_face(label: str, generation: store.Generation, image: Image.Image) -> None:
    cropped = crop_to_artwork(image)
    resized = cropped.resize(config.SELECTED_FACE_SIZE, Image.LANCZOS)
    face_path = config.LEGACY_FACE_DIR / f"{label}.png"
    resized.save(face_path, format="PNG", optimize=True)

    (config.GLYPH_DIR / f"{label}.png").unlink(missing_ok=True)
    (config.GLYPH_DIR / f"{label}.height.png").unlink(missing_ok=True)
    glyph_manifest = _load_manifest(config.GLYPH_MANIFEST)
    if label in glyph_manifest:
        glyph_manifest.pop(label)
        _save_manifest(config.GLYPH_MANIFEST, glyph_manifest)

    manifest = _load_manifest(config.SELECTED_MANIFEST)
    manifest[label] = {
        "kind": "legacy-face",
        "generation": generation.id,
        "run": generation.run_id,
        "provider": generation.params.get("provider", ""),
        "model": generation.params.get("model", ""),
        "style": generation.params.get("style", ""),
        "seed": generation.seed,
        "prompt": generation.prompt,
        "width": resized.width,
        "height": resized.height,
        "createdAt": generation.created_at,
        "source": str(generation.image_path.relative_to(config.REPO_ROOT)),
    }
    _save_manifest(config.SELECTED_MANIFEST, manifest)


def apply_selections(face_labels: list[str] | None = None, build: bool = True) -> list[str]:
    """Copy selected generations into the committed asset folders."""
    config.ensure_directories()
    selections = store.selected_generations()
    if face_labels is not None:
        wanted = set(face_labels)
        selections = {label: gen for label, gen in selections.items() if label in wanted}

    written: list[str] = []
    for label, generation in sorted(selections.items()):
        if not generation.exists():
            raise FileNotFoundError(
                f"Generation image for {label} is missing: {generation.image_path}"
            )
        data = generation.image_path.read_bytes()
        with Image.open(io.BytesIO(data)) as image:
            if glyphs.has_transparency(data):
                _apply_glyph(label, generation, image)
            else:
                _apply_legacy_face(label, generation, image)
        written.append(label)

    if build and written:
        rebuild_atlas()
    return written


def remove_faces(face_labels: list[str] | None = None, build: bool = True) -> list[str]:
    """Delete selected art so the vector fallback is used again."""
    glyph_manifest = _load_manifest(config.GLYPH_MANIFEST)
    legacy_manifest = _load_manifest(config.SELECTED_MANIFEST)

    if face_labels is None:
        targets = sorted(set(glyph_manifest) | set(legacy_manifest))
    else:
        targets = sorted(set(face_labels))

    removed: list[str] = []
    for label in targets:
        for path in (
            config.GLYPH_DIR / f"{label}.png",
            config.GLYPH_DIR / f"{label}.height.png",
            config.LEGACY_FACE_DIR / f"{label}.png",
        ):
            if path.exists():
                path.unlink()
                removed.append(label)
        glyph_manifest.pop(label, None)
        legacy_manifest.pop(label, None)

    _save_manifest(config.GLYPH_MANIFEST, glyph_manifest)
    _save_manifest(config.SELECTED_MANIFEST, legacy_manifest)

    if build:
        rebuild_atlas()
    return sorted(set(removed))


def rebuild_atlas() -> None:
    if shutil.which("dotnet") is None:
        raise RuntimeError("dotnet was not found on PATH; cannot rebuild the tile atlas.")
    subprocess.run(
        ["dotnet", "run", "--project", str(config.GENERATOR_PROJECT)],
        check=True,
        cwd=config.REPO_ROOT,
    )
