"""Applying selected generations to the committed game assets."""

from __future__ import annotations

import json
import shutil
import subprocess
from pathlib import Path

from PIL import Image, ImageChops

from . import config, store


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
    """Crop away the flat background margin around the painted tile.

    The model returns one tile floating on a plain background. Cropping to the
    tile bounds makes every face line up with the atlas cell instead of carrying
    its own random margin.
    """
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
    # If the artwork already bleeds to the edges there is nothing to trim.
    if box_width > width * 0.92 and box_height > height * 0.92:
        return rgb

    dx = int(box_width * inset)
    dy = int(box_height * inset)
    if box_width - 2 * dx < 16 or box_height - 2 * dy < 16:
        return rgb
    return rgb.crop((left + dx, top + dy, right - dx, bottom - dy))


def _normalize(source: Path, destination: Path) -> tuple[int, int]:
    destination.parent.mkdir(parents=True, exist_ok=True)
    with Image.open(source) as image:
        trimmed = crop_to_artwork(image)
        resized = trimmed.resize(config.SELECTED_FACE_SIZE, Image.LANCZOS)
        resized.save(destination, format="PNG", optimize=True)
        return resized.size


def apply_selections(face_labels: list[str] | None = None, build: bool = True) -> list[str]:
    """Copy selected generations into assets/art/faces and rebuild the atlas."""
    config.ensure_directories()
    selections = store.selected_generations()
    if face_labels is not None:
        wanted = set(face_labels)
        selections = {label: gen for label, gen in selections.items() if label in wanted}

    manifest_path = config.SELECTED_MANIFEST
    manifest: dict[str, dict] = {}
    if manifest_path.is_file():
        try:
            manifest = json.loads(manifest_path.read_text())
        except json.JSONDecodeError:
            manifest = {}

    written: list[str] = []
    for label, generation in sorted(selections.items()):
        if not generation.exists():
            raise FileNotFoundError(
                f"Generation image for {label} is missing: {generation.image_path}"
            )
        destination = config.SELECTED_DIR / f"{label}.png"
        width, height = _normalize(generation.image_path, destination)
        manifest[label] = {
            "generation": generation.id,
            "run": generation.run_id,
            "provider": generation.params.get("provider", ""),
            "model": generation.params.get("model", ""),
            "style": generation.params.get("style", ""),
            "seed": generation.seed,
            "prompt": generation.prompt,
            "width": width,
            "height": height,
            "createdAt": generation.created_at,
            "source": str(generation.image_path.relative_to(config.REPO_ROOT)),
        }
        written.append(label)

    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(json.dumps(dict(sorted(manifest.items())), indent=2) + "\n")

    if build and written:
        rebuild_atlas()
    return written


def remove_faces(face_labels: list[str] | None = None, build: bool = True) -> list[str]:
    """Delete selected face art so the vector fallback is used again."""
    manifest_path = config.SELECTED_MANIFEST
    manifest: dict[str, dict] = {}
    if manifest_path.is_file():
        try:
            manifest = json.loads(manifest_path.read_text())
        except json.JSONDecodeError:
            manifest = {}

    if face_labels is None:
        targets = sorted(manifest) or [
            path.stem for path in config.SELECTED_DIR.glob("*.png")
        ]
    else:
        targets = sorted(set(face_labels))

    removed: list[str] = []
    for label in targets:
        path = config.SELECTED_DIR / f"{label}.png"
        if path.exists():
            path.unlink()
            removed.append(label)
        manifest.pop(label, None)

    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(json.dumps(dict(sorted(manifest.items())), indent=2) + "\n")

    if build:
        rebuild_atlas()
    return removed


def rebuild_atlas() -> None:
    if shutil.which("dotnet") is None:
        raise RuntimeError("dotnet was not found on PATH; cannot rebuild the tile atlas.")
    subprocess.run(
        ["dotnet", "run", "--project", str(config.GENERATOR_PROJECT)],
        check=True,
        cwd=config.REPO_ROOT,
    )
