"""Style presets and prompt construction."""

from __future__ import annotations

from dataclasses import dataclass

from .faces import Face


@dataclass(frozen=True)
class Style:
    name: str
    description: str
    summary: str


STYLES: dict[str, Style] = {
    "porcelain": Style(
        name="porcelain",
        summary="Antique Chinese porcelain, vermilion and jade on ivory",
        description=(
            "a hand-painted antique Chinese porcelain mahjong tile with a softly "
            "beveled edge and a thin gold rule around the face, warm ivory glaze, "
            "deep vermilion red and jade green mineral pigments, fine gold-leaf "
            "outlines, subtle enamel texture"
        ),
    ),
    "lacquer": Style(
        name="lacquer",
        summary="Black lacquer with gold and cinnabar inlay",
        description=(
            "a carved Chinese black lacquer mahjong tile with a softly beveled "
            "edge, glossy dark lacquer ground, gold leaf and cinnabar red inlay, "
            "fine incised lines, subtle mother-of-pearl highlights"
        ),
    ),
    "ink": Style(
        name="ink",
        summary="Traditional ink wash on rice paper",
        description=(
            "a traditional Chinese ink-wash painting of a mahjong tile on warm "
            "rice paper, expressive brush strokes in black ink with a single "
            "vermilion seal red accent, visible paper fibre, restrained and elegant"
        ),
    ),
    "jade": Style(
        name="jade",
        summary="Carved jade with soft translucency",
        description=(
            "a carved pale jade mahjong tile with a softly beveled edge, soft "
            "translucent green-white stone, polished recessed carving, gentle "
            "internal glow, fine engraved outline, subtle gold inlay"
        ),
    ),
    "enamel": Style(
        name="enamel",
        summary="Cloisonne enamel with saturated colour",
        description=(
            "a cloisonne enamel mahjong tile with a softly beveled edge, saturated "
            "turquoise, vermilion and gold, thin gold wire outlines separating each "
            "colour field, glossy fired enamel surface, crisp graphic shapes"
        ),
    ),
    "minimal": Style(
        name="minimal",
        summary="Clean flat modern illustration",
        description=(
            "a clean modern flat illustration of a mahjong tile with a simple "
            "beveled edge, bold simple shapes, limited palette of vermilion, jade "
            "green and navy on warm ivory, crisp vector-clean edges, no texture"
        ),
    ),
}

DEFAULT_STYLE = "porcelain"

_NEGATIVE = (
    "different symbol, extra characters, missing character, wrong glyph, "
    "gibberish text, misspelled character, extra pips, missing pips, merged "
    "shapes, invented symbols, multiple tiles, tile stack, blurry, low contrast, "
    "perspective, drop shadow, busy background, photograph, human hands, "
    "watermark, signature, triangular tile, square tile, irregular tile outline, "
    "green strokes on a red character"
)


def build_prompt(face: Face, style: str = DEFAULT_STYLE, extra: str = "") -> tuple[str, str]:
    preset = STYLES.get(style)
    if preset is None:
        raise ValueError(
            f"Unknown style '{style}'. Choose from: {', '.join(sorted(STYLES))}"
        )
    prompt = (
        f"Repaint this mahjong tile as {preset.description}. "
        "Preserve the exact subject, layout, count and proportions of every "
        f"element: {face.description}. Do not add, remove or merge any element. "
        "One single upright tile with a rounded rectangular outline, exactly like the "
        "input, centred on a plain warm ivory background, flat straight-on front "
        "view, no perspective, crisp legible detail, consistent line weight."
    )
    if extra:
        prompt = f"{prompt} {extra.strip()}"
    return prompt, _NEGATIVE


_DECOMPOSE_PROMPT = (
    "Split this mahjong tile image into two layers. The foreground layer must "
    "contain only the glyph artwork on a fully transparent background, keeping "
    "the exact shapes, colours and proportions. The background layer contains "
    "the plain tile face. No tile body, no border and no shadow in the "
    "foreground layer."
)

_DECOMPOSE_NEGATIVE = (
    "extra symbols, wrong glyph, missing character, invented symbols, blurry, "
    "tile body, border, frame, shadow, watermark"
)


_DECOMPOSE_THREE_PROMPT = (
    "Split this mahjong tile into three layers. Layer 1: the plain background. "
    "Layer 2: the ceramic tile body with its gold border and bevel. Layer 3: "
    "only the painted glyph strokes on a fully transparent background, with no "
    "tile surface, no border and no shadow. Keep the exact shapes, colours and "
    "proportions of the glyph."
)


def build_decomposition_prompt(extra: str = "", layers: int = 2) -> tuple[str, str]:
    prompt = _DECOMPOSE_THREE_PROMPT if layers >= 3 else _DECOMPOSE_PROMPT
    if extra:
        prompt = f"{prompt} {extra.strip()}"
    return prompt, _DECOMPOSE_NEGATIVE


def style_summary(style: str) -> str:
    preset = STYLES.get(style)
    return preset.summary if preset else style
