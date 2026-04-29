from __future__ import annotations

import json
from typing import Any

import httpx

from app.config import Settings
from app.schemas import AnalyzeLabelResponse, NutritionFacts, ProductLabel
from app.services.image_utils import load_pil_image, normalize_image_bytes, parse_json_object, to_base64
from app.services.nutrition_lookup import NutritionLookup


LABEL_PROMPT = """Read this grocery product label. Return only JSON with:
{
  "name": "product name",
  "brand": "brand if visible",
  "quantity": "package size if visible",
  "serving_size": "serving size if visible",
  "calories": number or null,
  "fat_g": number or null,
  "carbohydrates_g": number or null,
  "sugars_g": number or null,
  "fiber_g": number or null,
  "protein_g": number or null,
  "sodium_mg": number or null,
  "ingredients": ["short ingredient names"],
  "allergens": ["visible allergens"],
  "claims": ["visible claims such as gluten free, organic"],
  "confidence": number from 0 to 1
}
Use null for values that are not visible. Do not invent nutrition facts."""


class LabelAnalyzer:
    def __init__(self, settings: Settings, lookup: NutritionLookup):
        self.settings = settings
        self.lookup = lookup

    async def analyze(self, image_bytes: bytes) -> AnalyzeLabelResponse:
        warnings: list[str] = []
        normalized_image = normalize_image_bytes(image_bytes)

        vlm_label = await self._try_vlm(normalized_image, warnings)

        product = vlm_label if vlm_label is not None else ProductLabel(source="No label source", confidence=0.0)
        mode = self.settings.ai_mode
        if vlm_label is None and mode == "mock":
            warnings.append("SMARTCART_AI_MODE=mock is active, so label-reading values are deterministic placeholders.")

        return AnalyzeLabelResponse(
            mode=mode,
            barcode=None,
            product=product,
            summary=build_summary(product),
            warnings=warnings,
        )

    async def _try_vlm(self, image_bytes: bytes, warnings: list[str]) -> ProductLabel | None:
        mode = self.settings.ai_mode
        if mode == "mock":
            return mock_label()
        if mode == "ollama":
            return await self._ollama_label(image_bytes)
        if mode in {"qwen", "qwen_transformers", "transformers"}:
            return await self._qwen_label(image_bytes)

        warnings.append(f"Unknown SMARTCART_AI_MODE={mode!r}; no label model is active.")
        return None

    async def _ollama_label(self, image_bytes: bytes) -> ProductLabel:
        payload = {
            "model": self.settings.ollama_model,
            "prompt": LABEL_PROMPT,
            "images": [to_base64(image_bytes)],
            "stream": False,
            "format": "json",
        }
        timeout = httpx.Timeout(self.settings.request_timeout_seconds)
        async with httpx.AsyncClient(timeout=timeout) as client:
            response = await client.post(f"{self.settings.ollama_base_url}/api/generate", json=payload)
            response.raise_for_status()
            data = response.json()

        parsed = parse_json_object(str(data.get("response", "")))
        return product_from_vlm_dict(parsed, "Ollama " + self.settings.ollama_model)

    async def _qwen_label(self, image_bytes: bytes) -> ProductLabel:
        return await run_in_threadpool_qwen(image_bytes, self.settings.qwen_model_id)


def mock_label() -> ProductLabel:
    return ProductLabel(
        name="Label ready for local VLM",
        brand="Point at a product label",
        quantity=None,
        nutrition=NutritionFacts(),
        claims=["mock mode"],
        confidence=0.25,
        source="Mock label analyzer",
    )


def product_from_vlm_dict(data: dict[str, Any], source: str) -> ProductLabel:
    return ProductLabel(
        name=str(data.get("name") or data.get("product_name") or "Unknown product").strip(),
        brand=optional_text(data.get("brand")),
        quantity=optional_text(data.get("quantity")),
        ingredients=string_list(data.get("ingredients")),
        allergens=string_list(data.get("allergens")),
        claims=string_list(data.get("claims")),
        nutrition=NutritionFacts(
            serving_size=optional_text(data.get("serving_size")),
            calories=optional_float(data.get("calories")),
            fat_g=optional_float(data.get("fat_g")),
            carbohydrates_g=optional_float(data.get("carbohydrates_g")),
            sugars_g=optional_float(data.get("sugars_g")),
            fiber_g=optional_float(data.get("fiber_g")),
            protein_g=optional_float(data.get("protein_g")),
            sodium_mg=optional_float(data.get("sodium_mg")),
        ),
        confidence=optional_float(data.get("confidence")) or 0.55,
        source=source,
        raw={"vlm": data},
    )


async def run_in_threadpool_qwen(image_bytes: bytes, model_id: str) -> ProductLabel:
    from fastapi.concurrency import run_in_threadpool

    return await run_in_threadpool(_qwen_label_sync, image_bytes, model_id)


_QWEN_MODEL: Any = None
_QWEN_PROCESSOR: Any = None


def _qwen_label_sync(image_bytes: bytes, model_id: str) -> ProductLabel:
    global _QWEN_MODEL, _QWEN_PROCESSOR

    import torch
    from transformers import AutoModelForImageTextToText, AutoProcessor

    if _QWEN_PROCESSOR is None:
        _QWEN_PROCESSOR = AutoProcessor.from_pretrained(model_id, trust_remote_code=True)
    if _QWEN_MODEL is None:
        dtype = torch.float16 if torch.cuda.is_available() else torch.float32
        _QWEN_MODEL = AutoModelForImageTextToText.from_pretrained(
            model_id,
            torch_dtype=dtype,
            device_map="auto",
            trust_remote_code=True,
        )

    image = load_pil_image(image_bytes)
    messages = [
        {
            "role": "user",
            "content": [
                {"type": "image", "image": image},
                {"type": "text", "text": LABEL_PROMPT},
            ],
        }
    ]
    text = _QWEN_PROCESSOR.apply_chat_template(messages, tokenize=False, add_generation_prompt=True)
    inputs = _QWEN_PROCESSOR(text=[text], images=[image], return_tensors="pt")
    inputs = {key: value.to(_QWEN_MODEL.device) for key, value in inputs.items()}

    with torch.inference_mode():
        generated_ids = _QWEN_MODEL.generate(**inputs, max_new_tokens=512)

    output = _QWEN_PROCESSOR.batch_decode(generated_ids, skip_special_tokens=True)[0]
    parsed = parse_json_object(output)
    if not parsed:
        parsed = {"name": "Unknown product", "confidence": 0.2, "raw_text": output}
    return product_from_vlm_dict(parsed, "Qwen local VLM")


def build_summary(product: ProductLabel) -> str:
    pieces = [product.name]
    if product.brand:
        pieces.append(f"by {product.brand}")
    if product.nutrition.calories is not None:
        pieces.append(f"{product.nutrition.calories:g} kcal")
    if product.nutrition.sugars_g is not None:
        pieces.append(f"{product.nutrition.sugars_g:g} g sugar")
    if product.allergens:
        pieces.append("allergens: " + ", ".join(product.allergens[:4]))
    return " | ".join(pieces)


def optional_text(value: Any) -> str | None:
    if value is None:
        return None
    text = str(value).strip()
    return text or None


def optional_float(value: Any) -> float | None:
    if value is None:
        return None
    try:
        return round(float(value), 2)
    except (TypeError, ValueError):
        return None


def string_list(value: Any) -> list[str]:
    if value is None:
        return []
    if isinstance(value, list):
        return [str(item).strip() for item in value if str(item).strip()][:12]
    if isinstance(value, str):
        try:
            decoded = json.loads(value)
            if isinstance(decoded, list):
                return string_list(decoded)
        except json.JSONDecodeError:
            pass
        return [part.strip() for part in value.split(",") if part.strip()][:12]
    return []
