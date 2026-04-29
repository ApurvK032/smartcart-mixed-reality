from __future__ import annotations

import base64
import io
import json
import re
from typing import Any

from PIL import Image, ImageOps


def normalize_image_bytes(image_bytes: bytes, *, max_side: int = 1280, jpeg_quality: int = 86) -> bytes:
    """Load an uploaded image, fix orientation, shrink it, and return JPEG bytes."""

    with Image.open(io.BytesIO(image_bytes)) as image:
        image = ImageOps.exif_transpose(image)
        image = image.convert("RGB")
        image.thumbnail((max_side, max_side))
        output = io.BytesIO()
        image.save(output, format="JPEG", quality=jpeg_quality, optimize=True)
        return output.getvalue()


def to_base64(image_bytes: bytes) -> str:
    return base64.b64encode(image_bytes).decode("ascii")


def load_pil_image(image_bytes: bytes) -> Image.Image:
    with Image.open(io.BytesIO(image_bytes)) as image:
        return ImageOps.exif_transpose(image).convert("RGB")


def parse_json_object(text: str) -> dict[str, Any]:
    """Best-effort parser for VLMs that wrap JSON in prose or markdown."""

    if not text:
        return {}

    cleaned = text.strip()
    if cleaned.startswith("```"):
        cleaned = re.sub(r"^```(?:json)?", "", cleaned, flags=re.IGNORECASE).strip()
        cleaned = re.sub(r"```$", "", cleaned).strip()

    try:
        value = json.loads(cleaned)
        return value if isinstance(value, dict) else {}
    except json.JSONDecodeError:
        pass

    match = re.search(r"\{.*\}", cleaned, flags=re.DOTALL)
    if not match:
        return {}

    try:
        value = json.loads(match.group(0))
        return value if isinstance(value, dict) else {}
    except json.JSONDecodeError:
        return {}

