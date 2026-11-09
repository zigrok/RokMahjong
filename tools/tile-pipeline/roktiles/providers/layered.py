"""Qwen-Image-Layered provider: decomposes a face reference into RGBA layers."""

from __future__ import annotations

from .. import config
from ..glyphs import choose_glyph_layer
from .base import GenerationOutput, GenerationRequest, ProviderError
from .comfyui import (
    _request_json,
    download_image,
    output_images,
    run_workflow,
    upload_image,
)


def build_layered_workflow(
    *,
    image_name: str,
    prompt: str,
    negative_prompt: str,
    seed: int,
    steps: int,
    cfg: float,
    layers: int,
    unet: str,
    clip: str,
    vae: str,
    shift: float,
    filename_prefix: str,
) -> dict:
    """The official Image-to-Layers (Qwen-Image-Layered) graph."""
    return {
        "1": {
            "class_type": "UNETLoader",
            "inputs": {"unet_name": unet, "weight_dtype": "default"},
        },
        "2": {
            "class_type": "CLIPLoader",
            "inputs": {"clip_name": clip, "type": "qwen_image", "device": "default"},
        },
        "3": {"class_type": "VAELoader", "inputs": {"vae_name": vae}},
        "4": {"class_type": "LoadImage", "inputs": {"image": image_name}},
        "5": {"class_type": "GetImageSize", "inputs": {"image": ["4", 0]}},
        "6": {
            "class_type": "EmptyQwenImageLayeredLatentImage",
            "inputs": {
                "width": ["5", 0],
                "height": ["5", 1],
                "layers": layers,
                "batch_size": 1,
            },
        },
        "7": {"class_type": "VAEEncode", "inputs": {"pixels": ["4", 0], "vae": ["3", 0]}},
        "8": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["2", 0], "text": prompt}},
        "9": {
            "class_type": "CLIPTextEncode",
            "inputs": {"clip": ["2", 0], "text": negative_prompt},
        },
        "10": {
            "class_type": "ReferenceLatent",
            "inputs": {"conditioning": ["8", 0], "latent": ["7", 0]},
        },
        "11": {
            "class_type": "ReferenceLatent",
            "inputs": {"conditioning": ["9", 0], "latent": ["7", 0]},
        },
        "12": {
            "class_type": "ModelSamplingAuraFlow",
            "inputs": {"model": ["1", 0], "shift": shift},
        },
        "13": {
            "class_type": "KSampler",
            "inputs": {
                "model": ["12", 0],
                "positive": ["10", 0],
                "negative": ["11", 0],
                "latent_image": ["6", 0],
                "seed": seed,
                "steps": steps,
                "cfg": cfg,
                "sampler_name": "euler",
                "scheduler": "simple",
                "denoise": 1.0,
            },
        },
        "14": {
            "class_type": "LatentCut",
            "inputs": {"samples": ["13", 0], "dim": "t", "index": 1, "amount": 16384},
        },
        "15": {
            "class_type": "LatentCutToBatch",
            "inputs": {"samples": ["14", 0], "dim": "t", "slice_size": 1},
        },
        "16": {"class_type": "VAEDecode", "inputs": {"samples": ["15", 0], "vae": ["3", 0]}},
        "17": {
            "class_type": "SaveImage",
            "inputs": {"images": ["16", 0], "filename_prefix": filename_prefix},
        },
    }


class LayeredProvider:
    """Produces a transparent glyph layer with Qwen-Image-Layered."""

    name = "layered"

    def __init__(
        self,
        model: str | None = None,
        *,
        clip: str | None = None,
        vae: str | None = None,
        base_url: str | None = None,
    ) -> None:
        self.model = model or config.COMFYUI_LAYERED_UNET
        self.clip = clip or config.COMFYUI_CLIP
        self.vae = vae or config.COMFYUI_LAYERED_VAE
        self.base_url = (base_url or config.COMFYUI_URL).rstrip("/")

    def health(self) -> dict:
        return _request_json(f"{self.base_url}/system_stats", timeout=10)

    def generate(self, request: GenerationRequest) -> GenerationOutput:
        options = request.options
        image_name = upload_image(self.base_url, request.reference)
        workflow = build_layered_workflow(
            image_name=image_name,
            prompt=request.prompt,
            negative_prompt=request.negative_prompt,
            seed=request.seed,
            steps=int(options.get("steps", 20)),
            cfg=float(options.get("cfg", 2.5)),
            layers=int(options.get("layers", 2)),
            unet=self.model,
            clip=self.clip,
            vae=self.vae,
            shift=float(options.get("shift", 1.0)),
            filename_prefix=str(options.get("filename_prefix", "roktiles_layered")),
        )

        prompt_id, entry = run_workflow(self.base_url, workflow)
        images = output_images(entry, "17")
        if not images:
            raise ProviderError(f"Qwen-Image-Layered produced no layers: {entry}")

        layers = [download_image(self.base_url, info) for info in images]
        index, best, stats = choose_glyph_layer(layers)
        chosen = layers[index]

        return GenerationOutput(
            image=chosen,
            width=best.width,
            height=best.height,
            meta={
                "prompt_id": prompt_id,
                "unet": self.model,
                "clip": self.clip,
                "vae": self.vae,
                "layers": len(layers),
                "glyph_layer": index,
                "layer_stats": [layer.as_dict() for layer in stats],
                "steps": int(options.get("steps", 20)),
                "cfg": float(options.get("cfg", 2.5)),
                "background": "transparent",
                "has_alpha": True,
            },
        )
