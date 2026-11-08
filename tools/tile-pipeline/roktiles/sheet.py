"""Contact sheets for reviewing generations without a GUI."""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

from . import config, store

THUMB = (208, 288)
LABEL_HEIGHT = 34
PADDING = 12
COLUMNS = 6


def _font() -> ImageFont.ImageFont:
    return ImageFont.load_default()


def build_sheet(
    generations: list[store.Generation],
    output: Path,
    columns: int = COLUMNS,
) -> Path:
    if not generations:
        raise ValueError("No generations to place on a sheet.")

    selected = store.selection_generation_ids()
    columns = max(1, min(columns, len(generations)))
    rows = (len(generations) + columns - 1) // columns
    cell_width = THUMB[0] + PADDING
    cell_height = THUMB[1] + LABEL_HEIGHT + PADDING
    sheet = Image.new(
        "RGB",
        (columns * cell_width + PADDING, rows * cell_height + PADDING),
        (24, 30, 28),
    )
    draw = ImageDraw.Draw(sheet)
    font = _font()

    for index, generation in enumerate(generations):
        row, column = divmod(index, columns)
        x = PADDING + column * cell_width
        y = PADDING + row * cell_height
        try:
            with Image.open(generation.image_path) as image:
                thumb = image.convert("RGB").resize(THUMB, Image.LANCZOS)
        except OSError:
            draw.rectangle([x, y, x + THUMB[0], y + THUMB[1]], fill=(60, 20, 20))
            thumb = None
        if thumb is not None:
            sheet.paste(thumb, (x, y))

        is_selected = selected.get(generation.face) == generation.id
        border = (255, 210, 90) if is_selected else (90, 100, 95)
        draw.rectangle(
            [x - 1, y - 1, x + THUMB[0], y + THUMB[1]], outline=border, width=3
        )

        marker = "* " if is_selected else ""
        lines = [
            f"{marker}{generation.face}  {generation.id}",
            f"{generation.params.get('style', '-')}  seed {generation.seed}",
        ]
        for line_index, line in enumerate(lines):
            draw.text(
                (x + 2, y + THUMB[1] + 3 + line_index * 14),
                line,
                fill=(235, 235, 225),
                font=font,
            )

    output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(output, format="PNG", optimize=True)
    return output


def default_output(name: str = "sheet") -> Path:
    from datetime import datetime

    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    return config.ARTIFACTS / "sheets" / f"{name}-{stamp}.png"
