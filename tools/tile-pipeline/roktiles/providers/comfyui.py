"""ComfyUI provider using Qwen-Image-Edit 2511 (GGUF) as a structure-preserving edit."""

from __future__ import annotations

import json
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path

from .. import config
from .base import GenerationOutput, GenerationRequest, ProviderError


def _request_json(url: str, payload: dict | None = None, timeout: int = 30) -> dict:
    data = None
    headers = {}
    if payload is not None:
        data = json.dumps(payload).encode("utf-8")
        headers["Content-Type"] = "application/json"
    request = urllib.request.Request(url, data=data, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:  # pragma: no cover - network path
        detail = error.read().decode("utf-8", "replace")
        raise ProviderError(f"ComfyUI returned HTTP {error.code}: {detail}") from error
    except urllib.error.URLError as error:  # pragma: no cover - network path
        raise ProviderError(
            f"ComfyUI is not reachable at {config.COMFYUI_URL}. Start it first. ({error.reason})"
        ) from error


def _post_multipart(url: str, fields: dict[str, str], file_field: str, path: Path) -> dict:
    boundary = f"----roktiles{uuid.uuid4().hex}"
    body = bytearray()
    for name, value in fields.items():
        body.extend(f"--{boundary}\r\n".encode())
        body.extend(f'Content-Disposition: form-data; name="{name}"\r\n\r\n'.encode())
        body.extend(str(value).encode())
        body.extend(b"\r\n")
    body.extend(f"--{boundary}\r\n".encode())
    body.extend(
        f'Content-Disposition: form-data; name="{file_field}"; filename="{path.name}"\r\n'.encode()
    )
    body.extend(b"Content-Type: image/png\r\n\r\n")
    body.extend(path.read_bytes())
    body.extend(b"\r\n")
    body.extend(f"--{boundary}--\r\n".encode())

    request = urllib.request.Request(
        url,
        data=bytes(body),
        headers={"Content-Type": f"multipart/form-data; boundary={boundary}"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:  # pragma: no cover - network path
        detail = error.read().decode("utf-8", "replace")
        raise ProviderError(f"ComfyUI upload failed with HTTP {error.code}: {detail}") from error


def build_workflow(
    *,
    image_name: str,
    prompt: str,
    negative_prompt: str,
    seed: int,
    steps: int,
    cfg: float,
    denoise: float,
    sampler: str,
    scheduler: str,
    unet: str,
    clip: str,
    vae: str,
    shift: float,
    filename_prefix: str,
    lora: str | None = None,
    lora_strength: float = 1.0,
) -> dict:
    model_source: list = ["1", 0]
    workflow: dict = {
        "1": {"class_type": "UnetLoaderGGUF", "inputs": {"unet_name": unet}},
        "2": {
            "class_type": "CLIPLoader",
            "inputs": {"clip_name": clip, "type": "qwen_image", "device": "default"},
        },
        "3": {"class_type": "VAELoader", "inputs": {"vae_name": vae}},
        "4": {
            "class_type": "ModelSamplingAuraFlow",
            "inputs": {"model": model_source, "shift": shift},
        },
        "5": {"class_type": "CFGNorm", "inputs": {"model": ["4", 0], "strength": 1.0}},
        "6": {"class_type": "LoadImage", "inputs": {"image": image_name}},
        "7": {"class_type": "FluxKontextImageScale", "inputs": {"image": ["6", 0]}},
        "8": {
            "class_type": "TextEncodeQwenImageEditPlus",
            "inputs": {
                "clip": ["2", 0],
                "vae": ["3", 0],
                "image1": ["7", 0],
                "prompt": prompt,
            },
        },
        "9": {
            "class_type": "TextEncodeQwenImageEditPlus",
            "inputs": {
                "clip": ["2", 0],
                "vae": ["3", 0],
                "image1": ["7", 0],
                "prompt": negative_prompt,
            },
        },
        "10": {
            "class_type": "FluxKontextMultiReferenceLatentMethod",
            "inputs": {"conditioning": ["9", 0], "reference_latents_method": "index_timestep_zero"},
        },
        "11": {
            "class_type": "FluxKontextMultiReferenceLatentMethod",
            "inputs": {"conditioning": ["8", 0], "reference_latents_method": "index_timestep_zero"},
        },
        "12": {"class_type": "VAEEncode", "inputs": {"pixels": ["7", 0], "vae": ["3", 0]}},
        "13": {
            "class_type": "KSampler",
            "inputs": {
                "model": ["5", 0],
                "positive": ["11", 0],
                "negative": ["10", 0],
                "latent_image": ["12", 0],
                "seed": seed,
                "steps": steps,
                "cfg": cfg,
                "sampler_name": sampler,
                "scheduler": scheduler,
                "denoise": denoise,
            },
        },
        "14": {"class_type": "VAEDecode", "inputs": {"samples": ["13", 0], "vae": ["3", 0]}},
        "15": {
            "class_type": "SaveImage",
            "inputs": {"images": ["14", 0], "filename_prefix": filename_prefix},
        },
    }
    if lora:
        workflow["16"] = {
            "class_type": "LoraLoaderModelOnly",
            "inputs": {
                "model": ["1", 0],
                "lora_name": lora,
                "strength_model": lora_strength,
            },
        }
        workflow["4"]["inputs"]["model"] = ["16", 0]
    return workflow


class ComfyUIProvider:
    name = "comfyui"

    def __init__(
        self,
        model: str | None = None,
        *,
        clip: str | None = None,
        vae: str | None = None,
        base_url: str | None = None,
    ) -> None:
        self.model = model or config.COMFYUI_UNET
        self.clip = clip or config.COMFYUI_CLIP
        self.vae = vae or config.COMFYUI_VAE
        self.lora = config.COMFYUI_LORA
        self.base_url = (base_url or config.COMFYUI_URL).rstrip("/")

    def health(self) -> dict:
        return _request_json(f"{self.base_url}/system_stats", timeout=10)

    def generate(self, request: GenerationRequest) -> GenerationOutput:
        options = request.options
        image_name = upload_image(self.base_url, request.reference)
        workflow = build_workflow(
            image_name=image_name,
            prompt=request.prompt,
            negative_prompt=request.negative_prompt,
            seed=request.seed,
            steps=int(options.get("steps", 20)),
            cfg=float(options.get("cfg", 4.0)),
            denoise=float(options.get("denoise", 1.0)),
            sampler=str(options.get("sampler", "euler")),
            scheduler=str(options.get("scheduler", "simple")),
            unet=self.model,
            clip=self.clip,
            vae=self.vae,
            shift=float(options.get("shift", 3.1)),
            filename_prefix=str(options.get("filename_prefix", "roktiles")),
            lora=options.get("lora") or None,
            lora_strength=float(options.get("lora_strength", 1.0)),
        )

        prompt_id, entry = run_workflow(self.base_url, workflow)
        images = output_images(entry, "15")
        if not images:
            raise ProviderError(f"ComfyUI produced no image: {entry}")
        image_info = images[0]
        data = download_image(self.base_url, image_info)

        return GenerationOutput(
            image=data,
            width=request.width,
            height=request.height,
            meta={
                "prompt_id": prompt_id,
                "comfy_filename": image_info["filename"],
                "unet": self.model,
                "clip": self.clip,
                "vae": self.vae,
                "steps": int(options.get("steps", 20)),
                "cfg": float(options.get("cfg", 4.0)),
                "denoise": float(options.get("denoise", 1.0)),
                "sampler": str(options.get("sampler", "euler")),
                "scheduler": str(options.get("scheduler", "simple")),
                "shift": float(options.get("shift", 3.1)),
                "lora": options.get("lora") or None,
                "lora_strength": float(options.get("lora_strength", 1.0)),
            },
        )


def upload_image(base_url: str, path: Path) -> str:
    upload = _post_multipart(f"{base_url}/upload/image", {"overwrite": "true"}, "image", path)
    name = upload.get("name")
    if not name:
        raise ProviderError(f"ComfyUI upload returned no filename: {upload}")
    subfolder = upload.get("subfolder") or ""
    return f"{subfolder}/{name}" if subfolder else name


def run_workflow(base_url: str, workflow: dict) -> tuple[str, dict]:
    client_id = uuid.uuid4().hex
    queued = _request_json(f"{base_url}/prompt", {"prompt": workflow, "client_id": client_id})
    prompt_id = queued.get("prompt_id")
    if not prompt_id:
        raise ProviderError(f"ComfyUI rejected the workflow: {queued}")

    deadline = time.monotonic() + config.COMFYUI_TIMEOUT
    while time.monotonic() < deadline:
        history = _request_json(f"{base_url}/history/{prompt_id}", timeout=30)
        if prompt_id in history:
            entry = history[prompt_id]
            status = entry.get("status", {})
            if status.get("status_str") == "error":
                raise ProviderError(f"ComfyUI generation failed: {status.get('messages')}")
            if status.get("completed") or entry.get("outputs"):
                return prompt_id, entry
        time.sleep(1.0)

    raise ProviderError(f"ComfyUI generation timed out after {config.COMFYUI_TIMEOUT}s")


def output_images(entry: dict, node_id: str) -> list[dict]:
    return (entry.get("outputs", {}).get(node_id, {}).get("images")) or []


def download_image(base_url: str, image_info: dict) -> bytes:
    query = urllib.parse.urlencode(
        {
            "filename": image_info["filename"],
            "subfolder": image_info.get("subfolder", ""),
            "type": image_info.get("type", "output"),
        }
    )
    try:
        with urllib.request.urlopen(f"{base_url}/view?{query}", timeout=120) as response:
            return response.read()
    except urllib.error.HTTPError as error:  # pragma: no cover - network path
        raise ProviderError(f"Could not download ComfyUI image: {error}") from error
