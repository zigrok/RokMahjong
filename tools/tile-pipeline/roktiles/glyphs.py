"""Glyph layer selection and height-map derivation."""

from __future__ import annotations

import io
from dataclasses import dataclass

from PIL import Image, ImageFilter


@dataclass(frozen=True)
class LayerStats:
    index: int
    width: int
    height: int
    coverage: float
    transparency: float

    def as_dict(self) -> dict:
        return {
            "index": self.index,
            "width": self.width,
            "height": self.height,
            "coverage": round(self.coverage, 4),
            "transparency": round(self.transparency, 4),
        }


def _stats(index: int, data: bytes) -> LayerStats:
    with Image.open(io.BytesIO(data)) as image:
        rgba = image.convert("RGBA")
        alpha = rgba.getchannel("A")
        histogram = alpha.histogram()
        total = rgba.width * rgba.height
        ink = sum(histogram[13:])
        clear = sum(histogram[:8])
        return LayerStats(
            index=index,
            width=rgba.width,
            height=rgba.height,
            coverage=ink / total if total else 0.0,
            transparency=clear / total if total else 0.0,
        )


def analyse(layers: list[bytes]) -> list[LayerStats]:
    return [_stats(index, data) for index, data in enumerate(layers)]


def choose_glyph_layer(layers: list[bytes]) -> tuple[int, LayerStats, list[LayerStats]]:
    """Pick the foreground glyph layer from a layered decomposition.

    The background layer is fully opaque, so a usable glyph layer has both a
    meaningful transparent area and some ink. If no layer qualifies, fall back
    to the most transparent one.
    """
    if not layers:
        raise ValueError("No layers to choose from.")

    stats = analyse(layers)
    # A decomposition can yield the tile body and the glyph as separate
    # transparent layers. The glyph is the smaller substantial one.
    candidates = [
        layer
        for layer in stats
        if layer.transparency > 0.02 and 0.02 < layer.coverage < 0.9
    ]
    best = (
        min(candidates, key=lambda layer: layer.coverage)
        if candidates
        else max(stats, key=lambda layer: layer.transparency)
    )
    return best.index, best, stats


def has_transparency(data: bytes) -> bool:
    try:
        stats = _stats(0, data)
    except OSError:
        return False
    return stats.transparency > 0.02


def clean_alpha(image: Image.Image, low: int = 28, high: int = 210) -> Image.Image:
    """Drop the faint residual alpha left by the layered decomposition."""
    alpha = image.getchannel("A")
    span = max(1, high - low)
    lookup = [
        0 if value <= low else 255 if value >= high else int((value - low) * 255 / span)
        for value in range(256)
    ]
    return alpha.point(lookup)


def derive_height(image: Image.Image, blur_radius: int = 9) -> Image.Image:
    """Turn a glyph alpha channel into a soft height field for the 3D face."""
    alpha = image.convert("RGBA").getchannel("A")
    return alpha.filter(ImageFilter.GaussianBlur(blur_radius)).convert("L")
