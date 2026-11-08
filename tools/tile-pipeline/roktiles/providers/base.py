"""Provider protocol shared by ComfyUI and hosted APIs."""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Protocol


@dataclass(frozen=True)
class GenerationRequest:
    reference: Path
    prompt: str
    negative_prompt: str
    seed: int
    width: int = 832
    height: int = 1152
    options: dict = field(default_factory=dict)


@dataclass(frozen=True)
class GenerationOutput:
    image: bytes
    width: int
    height: int
    meta: dict


class Provider(Protocol):
    name: str
    model: str

    def generate(self, request: GenerationRequest) -> GenerationOutput: ...


class ProviderError(RuntimeError):
    """Raised when a provider cannot complete a generation."""
