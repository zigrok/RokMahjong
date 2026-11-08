"""The 42 traditional mahjong faces and their generation descriptions."""

from __future__ import annotations

from dataclasses import dataclass

NUMERALS = ["一", "二", "三", "四", "五", "六", "七", "八", "九"]
WINDS = [
    ("E", "東", "east"),
    ("S", "南", "south"),
    ("W", "西", "west"),
    ("N", "北", "north"),
]
FLOWERS = [
    ("F1", "梅", "plum blossom"),
    ("F2", "蘭", "orchid"),
    ("F3", "菊", "chrysanthemum"),
    ("F4", "竹", "bamboo"),
]
SEASONS = [
    ("Se1", "春", "spring"),
    ("Se2", "夏", "summer"),
    ("Se3", "秋", "autumn"),
    ("Se4", "冬", "winter"),
]


@dataclass(frozen=True)
class Face:
    label: str
    group: str
    rank: int
    description: str

    @property
    def title(self) -> str:
        return f"{self.group} {self.rank}"


def _characters() -> list[Face]:
    return [
        Face(
            f"C{rank}",
            "characters",
            rank,
            f"the Chinese numeral {NUMERALS[rank - 1]} stacked above the Chinese "
            "character 萬, every stroke of both characters in the same deep vermilion "
            "red with no green, split by a thin horizontal divider line",
        )
        for rank in range(1, 10)
    ]


def _bamboo() -> list[Face]:
    layouts = {
        1: "a single green bamboo stalk with two leaves",
        2: "two green bamboo stalks stacked vertically",
        3: "three green bamboo stalks, one centred above two side by side",
        4: "four green bamboo stalks in a two-by-two grid",
        5: "five green bamboo stalks, one at each corner and one in the centre",
        6: "six green bamboo stalks in two columns of three",
        7: "seven green bamboo stalks, one on top of two rows of three",
        8: "eight green bamboo stalks in two columns of four",
        9: "nine green bamboo stalks in a three-by-three grid",
    }
    return [Face(f"B{r}", "bamboo", r, layouts[r]) for r in range(1, 10)]


def _circles() -> list[Face]:
    layouts = {
        1: "one large circle pip made of concentric rings",
        2: "two circle pips stacked vertically",
        3: "three circle pips in a diagonal line",
        4: "four circle pips in a two-by-two grid",
        5: "five circle pips, one at each corner and one in the centre",
        6: "six circle pips in two columns of three",
        7: "seven circle pips, one on top of two rows of three",
        8: "eight circle pips in two columns of four",
        9: "nine circle pips in a three-by-three grid",
    }
    return [
        Face(
            f"D{r}",
            "circles",
            r,
            f"{layouts[r]}, the pips drawn as clean concentric rings in alternating "
            "vermilion red, jade green and navy blue",
        )
        for r in range(1, 10)
    ]


def _winds() -> list[Face]:
    return [
        Face(
            label,
            "winds",
            index + 1,
            f"the Chinese character {glyph} ({name} wind) in navy blue",
        )
        for index, (label, glyph, name) in enumerate(WINDS)
    ]


def _dragons() -> list[Face]:
    return [
        Face("R", "dragons", 1, "the Chinese character 中 in deep vermilion red"),
        Face("G", "dragons", 2, "the Chinese character 發 in jade green"),
        Face(
            "Wh",
            "dragons",
            3,
            "a bold blue rectangular frame around an empty ivory centre, with a "
            "small blue dot in the middle of the frame",
        ),
    ]


def _flowers() -> list[Face]:
    motifs = {
        "F1": ("a five-petal plum blossom", "rose pink"),
        "F2": ("an orchid spray with slender petals", "violet purple"),
        "F3": ("a round chrysanthemum with many petals", "golden amber"),
        "F4": ("a bamboo stem with two leaves", "jade green"),
    }
    return [
        Face(
            label,
            "flowers",
            index + 1,
            f"the Chinese character {glyph} above {motifs[label][0]}, both in "
            f"{motifs[label][1]}",
        )
        for index, (label, glyph, _) in enumerate(FLOWERS)
    ]


def _seasons() -> list[Face]:
    motifs = {
        "Se1": ("a sprouting bamboo stem with two leaves", "jade green"),
        "Se2": ("a sun disc with radiating rays", "warm orange"),
        "Se3": ("a single autumn leaf", "amber brown"),
        "Se4": ("a six-armed snowflake", "pale ice blue"),
    }
    return [
        Face(
            label,
            "seasons",
            index + 1,
            f"the Chinese character {glyph} above {motifs[label][0]}, both in "
            f"{motifs[label][1]}",
        )
        for index, (label, glyph, _) in enumerate(SEASONS)
    ]


FACES: list[Face] = [
    *_characters(),
    *_bamboo(),
    *_circles(),
    *_winds(),
    *_dragons(),
    *_flowers(),
    *_seasons(),
]

BY_LABEL = {face.label: face for face in FACES}
GROUPS = ("characters", "bamboo", "circles", "winds", "dragons", "flowers", "seasons")


def resolve_labels(
    labels: list[str] | None,
    group: str | None = None,
    every: bool = False,
) -> list[Face]:
    if every:
        return list(FACES)
    if group:
        if group not in GROUPS:
            raise ValueError(f"Unknown group '{group}'. Choose from: {', '.join(GROUPS)}")
        return [face for face in FACES if face.group == group]
    if not labels:
        raise ValueError("Choose --face LABEL, --group GROUP or --all.")
    unknown = [label for label in labels if label not in BY_LABEL]
    if unknown:
        raise ValueError(
            f"Unknown face label(s): {', '.join(unknown)}. Run `roktiles faces`."
        )
    return [BY_LABEL[label] for label in labels]
