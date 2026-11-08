"""OpenRouter image-model provider (optional, disabled without an API key)."""

from __future__ import annotations

import base64
import json
import urllib.error
import urllib.request

from .. import config
from .base import GenerationOutput, GenerationRequest, ProviderError


class OpenRouterProvider:
    name = "openrouter"

    def __init__(
        self,
        model: str | None = None,
        *,
        base_url: str | None = None,
        api_key: str | None = None,
    ) -> None:
        self.model = model or config.OPENROUTER_MODEL
        self.base_url = (base_url or config.OPENROUTER_BASE_URL).rstrip("/")
        self.api_key = api_key or config.OPENROUTER_API_KEY

    def available(self) -> bool:
        return bool(self.api_key)

    def generate(self, request: GenerationRequest) -> GenerationOutput:
        if not self.api_key:
            raise ProviderError(
                "OPENROUTER_API_KEY is not set. Export it to use the OpenRouter "
                "provider, or use --provider comfyui."
            )
        media_type = "image/png"
        encoded = base64.b64encode(request.reference.read_bytes()).decode("ascii")
        payload = {
            "model": self.model,
            "modalities": ["image", "text"],
            "messages": [
                {
                    "role": "user",
                    "content": [
                        {"type": "text", "text": request.prompt},
                        {
                            "type": "image_url",
                            "image_url": {"url": f"data:{media_type};base64,{encoded}"},
                        },
                    ],
                }
            ],
        }
        http_request = urllib.request.Request(
            f"{self.base_url}/chat/completions",
            data=json.dumps(payload).encode("utf-8"),
            headers={
                "Authorization": f"Bearer {self.api_key}",
                "Content-Type": "application/json",
                "HTTP-Referer": "https://github.com/zigrok/RokMahjong",
                "X-Title": "Rok Mahjong Tile Pipeline",
            },
            method="POST",
        )
        try:
            with urllib.request.urlopen(http_request, timeout=config.COMFYUI_TIMEOUT) as response:
                body = json.load(response)
        except urllib.error.HTTPError as error:  # pragma: no cover - network path
            detail = error.read().decode("utf-8", "replace")
            raise ProviderError(
                f"OpenRouter returned HTTP {error.code}: {detail}"
            ) from error
        except urllib.error.URLError as error:  # pragma: no cover - network path
            raise ProviderError(f"OpenRouter is not reachable: {error.reason}") from error

        image_bytes = self._extract_image(body)
        return GenerationOutput(
            image=image_bytes,
            width=request.width,
            height=request.height,
            meta={
                "model": self.model,
                "usage": body.get("usage", {}),
                "finish_reason": (
                    body.get("choices", [{}])[0].get("finish_reason")
                    if body.get("choices")
                    else None
                ),
            },
        )

    @staticmethod
    def _extract_image(body: dict) -> bytes:
        choices = body.get("choices") or []
        if not choices:
            raise ProviderError(f"OpenRouter returned no choices: {body}")
        message = choices[0].get("message", {})

        for image in message.get("images") or []:
            url = None
            if isinstance(image, dict):
                url = (image.get("image_url") or {}).get("url")
                url = url or image.get("url")
            if url:
                return OpenRouterProvider._decode_data_url(url)

        content = message.get("content")
        if isinstance(content, list):
            for part in content:
                if isinstance(part, dict) and part.get("type") == "image_url":
                    return OpenRouterProvider._decode_data_url(
                        (part.get("image_url") or {}).get("url", "")
                    )

        raise ProviderError(
            "OpenRouter response contained no image. The model may not support "
            "image output; set OPENROUTER_MODEL to an image-capable model."
        )

    @staticmethod
    def _decode_data_url(url: str) -> bytes:
        if url.startswith("data:"):
            _, _, data = url.partition(",")
            return base64.b64decode(data)
        with urllib.request.urlopen(url, timeout=120) as response:
            return response.read()
