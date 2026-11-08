"""Tests for the tile generation pipeline (no network, no ComfyUI)."""

from __future__ import annotations

import base64
import json

import pytest
from PIL import Image, ImageDraw

from roktiles import applier, config, faces, prompts, store
from roktiles.providers.base import ProviderError
from roktiles.providers.comfyui import build_workflow
from roktiles.providers.openrouter import OpenRouterProvider


def test_catalog_has_42_unique_faces() -> None:
    labels = [face.label for face in faces.FACES]
    assert len(labels) == 42
    assert len(set(labels)) == 42
    assert set(faces.GROUPS) == {face.group for face in faces.FACES}
    assert sum(face.group == "flowers" for face in faces.FACES) == 4
    assert sum(face.group == "seasons" for face in faces.FACES) == 4


def test_every_face_builds_a_prompt_with_its_subject() -> None:
    for face in faces.FACES:
        prompt, negative = prompts.build_prompt(face)
        assert face.description in prompt
        assert "Preserve the exact subject" in prompt
        assert negative


def test_unknown_style_is_rejected() -> None:
    with pytest.raises(ValueError):
        prompts.build_prompt(faces.FACES[0], "nonexistent")


def test_resolve_labels_by_group() -> None:
    bamboo = faces.resolve_labels(None, "bamboo")
    assert [face.label for face in bamboo] == [f"B{rank}" for rank in range(1, 10)]
    with pytest.raises(ValueError):
        faces.resolve_labels(["Z9"])


def test_comfyui_workflow_wires_the_qwen_edit_graph() -> None:
    workflow = build_workflow(
        image_name="C1.png",
        prompt="paint it",
        negative_prompt="bad",
        seed=7,
        steps=20,
        cfg=4.0,
        denoise=1.0,
        sampler="euler",
        scheduler="simple",
        unet="qwen.gguf",
        clip="clip.safetensors",
        vae="vae.safetensors",
        shift=3.1,
        filename_prefix="roktiles",
    )
    assert workflow["1"]["class_type"] == "UnetLoaderGGUF"
    assert workflow["6"]["inputs"]["image"] == "C1.png"
    assert workflow["13"]["inputs"]["positive"] == ["11", 0]
    assert workflow["13"]["inputs"]["negative"] == ["10", 0]
    assert workflow["13"]["inputs"]["seed"] == 7
    assert workflow["15"]["class_type"] == "SaveImage"
    for node in workflow.values():
        for value in node["inputs"].values():
            if isinstance(value, list):
                assert value[0] in workflow


def test_openrouter_extracts_a_base64_image() -> None:
    payload = b"\x89PNG\r\n\x1a\nfake"
    body = {
        "choices": [
            {
                "message": {
                    "images": [
                        {
                            "type": "image_url",
                            "image_url": {
                                "url": "data:image/png;base64,"
                                + base64.b64encode(payload).decode("ascii")
                            },
                        }
                    ]
                }
            }
        ]
    }
    assert OpenRouterProvider._extract_image(body) == payload


def test_openrouter_reports_a_missing_image() -> None:
    with pytest.raises(ProviderError):
        OpenRouterProvider._extract_image({"choices": [{"message": {"content": "hi"}}]})


def test_store_history_and_selection_roundtrip(tmp_path, monkeypatch) -> None:
    monkeypatch.setattr(config, "DB_PATH", tmp_path / "history.sqlite3")
    image = tmp_path / "face.png"
    Image.new("RGB", (8, 8), "white").save(image)

    store.create_run("r-test", "comfyui", "qwen", "porcelain", {"steps": 20})
    store.add_generation(
        generation_id="g-test",
        run_id="r-test",
        face="C1",
        image_path=image,
        width=8,
        height=8,
        prompt="p",
        negative_prompt="n",
        seed=1,
        params={"provider": "comfyui", "style": "porcelain"},
    )

    generation = store.get_generation("g-test")
    assert generation is not None
    assert generation.face == "C1"
    assert store.list_generations(face="C1")[0].id == "g-test"

    store.set_selection("C1", "g-test")
    selections = store.selected_generations()
    assert selections["C1"].id == "g-test"

    store.clear_selection("C1")
    assert store.selected_generations() == {}


def test_crop_to_artwork_crops_the_background_margin() -> None:
    image = Image.new("RGB", (400, 520), (250, 245, 215))
    draw = ImageDraw.Draw(image)
    draw.rectangle((60, 50, 340, 470), outline=(150, 110, 40), width=12)
    trimmed = applier.crop_to_artwork(image, inset=0.02)
    assert trimmed.size[0] < 300
    assert trimmed.size[1] < 430


def test_crop_to_artwork_keeps_full_bleed_art() -> None:
    image = Image.new("RGB", (400, 520), (200, 60, 60))
    assert applier.crop_to_artwork(image).size == (400, 520)


def test_apply_selections_writes_face_and_manifest(tmp_path, monkeypatch) -> None:
    repo = tmp_path / "repo"
    history = repo / "artifacts" / "tile-pipeline" / "history" / "r-test"
    history.mkdir(parents=True)
    selected = repo / "assets" / "art" / "faces"
    monkeypatch.setattr(config, "DB_PATH", repo / "history.sqlite3")
    monkeypatch.setattr(config, "REPO_ROOT", repo)
    monkeypatch.setattr(config, "SELECTED_DIR", selected)
    monkeypatch.setattr(config, "SELECTED_MANIFEST", selected / "manifest.json")

    source = history / "C1_g-test.png"
    Image.new("RGB", (832, 1152), (240, 230, 200)).save(source)

    store.create_run("r-test", "comfyui", "qwen", "porcelain", {})
    store.add_generation(
        generation_id="g-test",
        run_id="r-test",
        face="C1",
        image_path=source,
        width=832,
        height=1152,
        prompt="p",
        negative_prompt="n",
        seed=42,
        params={"provider": "comfyui", "model": "qwen", "style": "porcelain"},
    )
    store.set_selection("C1", "g-test")

    written = applier.apply_selections(build=False)
    assert written == ["C1"]

    with Image.open(selected / "C1.png") as image:
        assert image.size == config.SELECTED_FACE_SIZE

    manifest = json.loads((selected / "manifest.json").read_text())
    assert manifest["C1"]["generation"] == "g-test"
    assert manifest["C1"]["style"] == "porcelain"
    assert manifest["C1"]["seed"] == 42

    removed = applier.remove_faces(["C1"], build=False)
    assert removed == ["C1"]
    assert not (selected / "C1.png").exists()
