"""Tests for the tile generation pipeline (no network, no ComfyUI)."""

from __future__ import annotations

import base64
import io
import json

import pytest
from PIL import Image, ImageDraw

from roktiles import applier, config, faces, glyphs, prompts, store
from roktiles.providers.base import ProviderError
from roktiles.providers.comfyui import build_workflow
from roktiles.providers.layered import build_layered_workflow
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


def test_decomposition_prompt_asks_for_a_transparent_foreground() -> None:
    prompt, negative = prompts.build_decomposition_prompt()
    assert "transparent background" in prompt
    assert "foreground" in prompt
    assert "shadow" in negative


def test_unknown_style_is_rejected() -> None:
    with pytest.raises(ValueError):
        prompts.build_prompt(faces.FACES[0], "nonexistent")


def test_resolve_labels_by_group() -> None:
    bamboo = faces.resolve_labels(None, "bamboo")
    assert [face.label for face in bamboo] == [f"B{rank}" for rank in range(1, 10)]
    with pytest.raises(ValueError):
        faces.resolve_labels(["Z9"])


def _workflow(**overrides):
    parameters = dict(
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
    parameters.update(overrides)
    return build_workflow(**parameters)


def test_comfyui_workflow_wires_the_qwen_edit_graph() -> None:
    workflow = _workflow()
    assert workflow["1"]["class_type"] == "UnetLoaderGGUF"
    assert workflow["6"]["inputs"]["image"] == "C1.png"
    assert workflow["13"]["inputs"]["positive"] == ["11", 0]
    assert workflow["13"]["inputs"]["negative"] == ["10", 0]
    assert workflow["13"]["inputs"]["seed"] == 7
    assert workflow["15"]["class_type"] == "SaveImage"
    assert workflow["4"]["inputs"]["model"] == ["1", 0]
    for node in workflow.values():
        for value in node["inputs"].values():
            if isinstance(value, list):
                assert value[0] in workflow


def test_comfyui_workflow_can_apply_a_lora() -> None:
    workflow = _workflow(lora="lightning.safetensors", lora_strength=0.8)
    assert workflow["16"]["class_type"] == "LoraLoaderModelOnly"
    assert workflow["16"]["inputs"]["lora_name"] == "lightning.safetensors"
    assert workflow["16"]["inputs"]["strength_model"] == 0.8
    assert workflow["4"]["inputs"]["model"] == ["16", 0]


def test_layered_workflow_splits_layers_into_a_batch() -> None:
    workflow = build_layered_workflow(
        image_name="C1.png",
        prompt="separate the glyph from the background",
        negative_prompt="",
        seed=3,
        steps=20,
        cfg=2.5,
        layers=2,
        unet="layered-Q4_K_M.gguf",
        clip="clip.safetensors",
        vae="layered_vae.safetensors",
        shift=1.0,
        filename_prefix="roktiles_layered",
    )
    assert workflow["1"]["class_type"] == "UnetLoaderGGUF"
    assert workflow["6"]["class_type"] == "EmptyQwenImageLayeredLatentImage"
    assert workflow["6"]["inputs"]["layers"] == 2
    assert workflow["6"]["inputs"]["width"] == ["5", 0]
    assert workflow["13"]["inputs"]["latent_image"] == ["6", 0]
    assert workflow["15"]["class_type"] == "LatentCutToBatch"
    assert workflow["17"]["class_type"] == "SaveImage"
    for node in workflow.values():
        for value in node["inputs"].values():
            if isinstance(value, list):
                assert value[0] in workflow


def _png(image: Image.Image) -> bytes:
    buffer = io.BytesIO()
    image.save(buffer, format="PNG")
    return buffer.getvalue()


def test_choose_glyph_layer_picks_the_foreground() -> None:
    background = Image.new("RGBA", (64, 64), (250, 245, 220, 255))
    foreground = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    ImageDraw.Draw(foreground).rectangle((16, 16, 48, 48), fill=(180, 40, 60, 255))

    index, best, stats = glyphs.choose_glyph_layer([_png(background), _png(foreground)])
    assert index == 1
    assert best.index == 1
    assert best.transparency > 0.5
    assert len(stats) == 2


def test_choose_glyph_layer_ignores_near_empty_layers() -> None:
    background = Image.new("RGBA", (64, 64), (250, 245, 220, 255))
    empty = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    glyph = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    ImageDraw.Draw(glyph).rectangle((16, 16, 48, 48), fill=(180, 40, 60, 255))

    index, _best, _stats = glyphs.choose_glyph_layer(
        [_png(background), _png(empty), _png(glyph)]
    )
    assert index == 2


def test_choose_glyph_layer_prefers_the_glyph_over_the_body() -> None:
    background = Image.new("RGBA", (64, 64), (250, 245, 220, 255))
    body = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    ImageDraw.Draw(body).rectangle((4, 4, 60, 60), fill=(220, 200, 160, 255))
    glyph = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    ImageDraw.Draw(glyph).rectangle((24, 24, 40, 40), fill=(180, 40, 60, 255))

    index, _best, _stats = glyphs.choose_glyph_layer(
        [_png(background), _png(body), _png(glyph)]
    )
    assert index == 2


def test_clean_alpha_drops_faint_residual() -> None:
    image = Image.new("RGBA", (8, 8), (0, 0, 0, 0))
    image.putpixel((0, 0), (255, 0, 0, 10))
    image.putpixel((1, 0), (255, 0, 0, 120))
    cleaned = glyphs.clean_alpha(image)
    assert cleaned.getpixel((0, 0)) == 0
    assert cleaned.getpixel((1, 0)) > 0


def test_derive_height_tracks_the_glyph_alpha() -> None:
    image = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    ImageDraw.Draw(image).rectangle((16, 16, 48, 48), fill=(180, 40, 60, 255))
    height = glyphs.derive_height(image)
    assert height.mode == "L"
    assert height.getpixel((32, 32)) > 200
    assert height.getpixel((2, 2)) < 40


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
    assert store.selected_generations()["C1"].id == "g-test"

    store.clear_selection("C1")
    assert store.selected_generations() == {}


def test_crop_to_artwork_crops_the_background_margin() -> None:
    image = Image.new("RGB", (400, 520), (250, 245, 215))
    ImageDraw.Draw(image).rectangle((60, 50, 340, 470), outline=(150, 110, 40), width=12)
    trimmed = applier.crop_to_artwork(image, inset=0.02)
    assert trimmed.size[0] < 300
    assert trimmed.size[1] < 430


def test_crop_to_artwork_keeps_full_bleed_art() -> None:
    image = Image.new("RGB", (400, 520), (200, 60, 60))
    assert applier.crop_to_artwork(image).size == (400, 520)


def _patch_asset_paths(tmp_path, monkeypatch):
    repo = tmp_path / "repo"
    art = repo / "assets" / "art"
    history = repo / "artifacts" / "tile-pipeline" / "history" / "r-test"
    history.mkdir(parents=True)
    monkeypatch.setattr(config, "DB_PATH", repo / "history.sqlite3")
    monkeypatch.setattr(config, "REPO_ROOT", repo)
    monkeypatch.setattr(config, "ART_ROOT", art)
    monkeypatch.setattr(config, "REFERENCE_DIR", art / "reference")
    monkeypatch.setattr(config, "GLYPH_DIR", art / "glyphs")
    monkeypatch.setattr(config, "GLYPH_MANIFEST", art / "glyphs" / "manifest.json")
    monkeypatch.setattr(config, "LEGACY_FACE_DIR", art / "faces")
    monkeypatch.setattr(config, "SELECTED_DIR", art / "faces")
    monkeypatch.setattr(config, "SELECTED_MANIFEST", art / "faces" / "manifest.json")
    return repo, history, art


def _add_generation(history, source, params=None):
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
        params=params or {"provider": "comfyui", "model": "qwen", "style": "porcelain"},
    )
    store.set_selection("C1", "g-test")


def test_apply_selections_writes_a_legacy_face_and_manifest(tmp_path, monkeypatch) -> None:
    _repo, history, art = _patch_asset_paths(tmp_path, monkeypatch)
    source = history / "C1_g-test.png"
    Image.new("RGB", (832, 1152), (240, 230, 200)).save(source)
    _add_generation(history, source)

    written = applier.apply_selections(build=False)
    assert written == ["C1"]

    with Image.open(art / "faces" / "C1.png") as image:
        assert image.size == config.SELECTED_FACE_SIZE

    manifest = json.loads((art / "faces" / "manifest.json").read_text())
    assert manifest["C1"]["kind"] == "legacy-face"
    assert manifest["C1"]["generation"] == "g-test"

    removed = applier.remove_faces(["C1"], build=False)
    assert removed == ["C1"]
    assert not (art / "faces" / "C1.png").exists()


def test_apply_selections_writes_a_glyph_and_height_map(tmp_path, monkeypatch) -> None:
    _repo, history, art = _patch_asset_paths(tmp_path, monkeypatch)
    source = history / "C1_g-test.png"
    glyph_image = Image.new("RGBA", (512, 704), (0, 0, 0, 0))
    ImageDraw.Draw(glyph_image).rectangle((120, 200, 380, 500), fill=(180, 40, 60, 255))
    glyph_image.save(source)
    _add_generation(
        history,
        source,
        params={
            "provider": "layered",
            "model": "layered",
            "style": "porcelain",
            "glyph_layer": 1,
        },
    )

    written = applier.apply_selections(build=False)
    assert written == ["C1"]

    glyph_path = art / "glyphs" / "C1.png"
    assert glyph_path.is_file()
    with Image.open(glyph_path) as image:
        assert image.mode == "RGBA"
        assert image.size == config.GLYPH_SIZE
        assert image.getpixel((0, 0))[3] == 0
    assert (art / "glyphs" / "C1.height.png").is_file()
    assert not (art / "faces" / "C1.png").exists()

    manifest = json.loads((art / "glyphs" / "manifest.json").read_text())
    assert manifest["C1"]["kind"] == "glyph"
    assert manifest["C1"]["layer"] == 1
