"""Provider factory."""

from __future__ import annotations

from .base import GenerationOutput, GenerationRequest, Provider, ProviderError
from .comfyui import ComfyUIProvider
from .layered import LayeredProvider
from .openrouter import OpenRouterProvider

PROVIDERS = ("comfyui", "layered", "openrouter")


def create_provider(
    name: str,
    model: str | None = None,
    *,
    options: dict | None = None,
) -> Provider:
    options = options or {}
    if name == "comfyui":
        return ComfyUIProvider(
            model,
            clip=options.get("clip"),
            vae=options.get("vae"),
            base_url=options.get("base_url"),
        )
    if name == "layered":
        return LayeredProvider(
            model,
            clip=options.get("clip"),
            vae=options.get("layered_vae"),
            base_url=options.get("base_url"),
        )
    if name == "openrouter":
        return OpenRouterProvider(
            model,
            base_url=options.get("base_url"),
            api_key=options.get("api_key"),
        )
    raise ProviderError(f"Unknown provider '{name}'. Choose from: {', '.join(PROVIDERS)}")


__all__ = [
    "ComfyUIProvider",
    "LayeredProvider",
    "GenerationOutput",
    "GenerationRequest",
    "OpenRouterProvider",
    "PROVIDERS",
    "Provider",
    "ProviderError",
    "create_provider",
]
