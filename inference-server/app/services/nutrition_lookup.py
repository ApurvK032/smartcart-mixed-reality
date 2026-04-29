from __future__ import annotations

import re
from typing import Any

import httpx

from app.config import Settings
from app.schemas import NutritionFacts, ProductLabel


OPEN_FOOD_FACTS_PRODUCT_URL = "https://world.openfoodfacts.org/api/v0/product/{barcode}.json"
OPEN_FOOD_FACTS_SEARCH_URL = "https://world.openfoodfacts.org/cgi/search.pl"
USDA_SEARCH_URL = "https://api.nal.usda.gov/fdc/v1/foods/search"


GENERAL_FOOD_FALLBACKS: dict[str, ProductLabel] = {
    "banana": ProductLabel(
        name="Banana",
        brand="General food",
        quantity="1 medium fruit",
        nutrition=NutritionFacts(serving_size="1 medium banana", calories=105, carbohydrates_g=27, sugars_g=14, fiber_g=3, protein_g=1.3, sodium_mg=1),
        confidence=0.7,
        source="Local general nutrition fallback",
    ),
    "apple": ProductLabel(
        name="Apple",
        brand="General food",
        quantity="1 medium fruit",
        nutrition=NutritionFacts(serving_size="1 medium apple", calories=95, carbohydrates_g=25, sugars_g=19, fiber_g=4.4, protein_g=0.5, sodium_mg=2),
        confidence=0.7,
        source="Local general nutrition fallback",
    ),
    "white rice": ProductLabel(
        name="White rice, cooked",
        brand="General food",
        quantity="1 cup cooked",
        nutrition=NutritionFacts(serving_size="1 cup cooked", calories=205, carbohydrates_g=45, sugars_g=0.1, fiber_g=0.6, protein_g=4.3, sodium_mg=2),
        confidence=0.65,
        source="Local general nutrition fallback",
    ),
    "whole milk": ProductLabel(
        name="Whole milk",
        brand="General food",
        quantity="1 cup",
        nutrition=NutritionFacts(serving_size="1 cup", calories=149, fat_g=7.9, carbohydrates_g=12, sugars_g=12, protein_g=7.7, sodium_mg=105),
        confidence=0.65,
        source="Local general nutrition fallback",
    ),
}


class NutritionLookup:
    def __init__(self, settings: Settings):
        self.settings = settings

    async def by_barcode(self, barcode: str) -> ProductLabel | None:
        cleaned = re.sub(r"\D", "", barcode or "")
        if not cleaned:
            return None

        timeout = httpx.Timeout(self.settings.request_timeout_seconds)
        async with httpx.AsyncClient(timeout=timeout, headers={"User-Agent": "SmartCart-MR/1.0"}) as client:
            response = await client.get(OPEN_FOOD_FACTS_PRODUCT_URL.format(barcode=cleaned))
            response.raise_for_status()
            payload = response.json()

        if payload.get("status") != 1 or not isinstance(payload.get("product"), dict):
            return None

        label = self._from_open_food_facts(payload["product"])
        label.barcode = cleaned
        return label

    async def search(self, query: str) -> ProductLabel | None:
        normalized = normalize_query(query)
        if not normalized:
            return None

        fallback = GENERAL_FOOD_FALLBACKS.get(normalized)
        if fallback is not None:
            return fallback.model_copy(deep=True)

        for key, value in GENERAL_FOOD_FALLBACKS.items():
            if key in normalized or normalized in key:
                return value.model_copy(deep=True)

        label = await self._search_open_food_facts(normalized)
        if label is not None:
            return label

        if self.settings.usda_api_key:
            return await self._search_usda(normalized)

        return None

    async def _search_open_food_facts(self, query: str) -> ProductLabel | None:
        params = {
            "search_terms": query,
            "search_simple": "1",
            "action": "process",
            "json": "1",
            "page_size": "5",
        }
        timeout = httpx.Timeout(self.settings.request_timeout_seconds)
        async with httpx.AsyncClient(timeout=timeout, headers={"User-Agent": "SmartCart-MR/1.0"}) as client:
            response = await client.get(OPEN_FOOD_FACTS_SEARCH_URL, params=params)
            response.raise_for_status()
            payload = response.json()

        products = payload.get("products")
        if not isinstance(products, list):
            return None

        for product in products:
            if isinstance(product, dict) and product.get("product_name"):
                return self._from_open_food_facts(product)
        return None

    async def _search_usda(self, query: str) -> ProductLabel | None:
        params = {"api_key": self.settings.usda_api_key, "query": query, "pageSize": 5}
        timeout = httpx.Timeout(self.settings.request_timeout_seconds)
        async with httpx.AsyncClient(timeout=timeout, headers={"User-Agent": "SmartCart-MR/1.0"}) as client:
            response = await client.get(USDA_SEARCH_URL, params=params)
            response.raise_for_status()
            payload = response.json()

        foods = payload.get("foods")
        if not isinstance(foods, list) or not foods:
            return None

        food = foods[0]
        nutrients = food.get("foodNutrients") or []

        def nutrient(name: str) -> float | None:
            for item in nutrients:
                nutrient_name = str(item.get("nutrientName", "")).lower()
                if name in nutrient_name:
                    return to_float(item.get("value"))
            return None

        return ProductLabel(
            name=str(food.get("description") or query).title(),
            brand=food.get("brandName") or "USDA FoodData Central",
            nutrition=NutritionFacts(
                serving_size="100 g",
                calories=nutrient("energy"),
                protein_g=nutrient("protein"),
                fat_g=nutrient("total lipid"),
                carbohydrates_g=nutrient("carbohydrate"),
                sugars_g=nutrient("sugars"),
                fiber_g=nutrient("fiber"),
                sodium_mg=nutrient("sodium"),
            ),
            confidence=0.68,
            source="USDA FoodData Central",
        )

    def _from_open_food_facts(self, product: dict[str, Any]) -> ProductLabel:
        nutriments = product.get("nutriments") if isinstance(product.get("nutriments"), dict) else {}
        ingredients = split_list(product.get("ingredients_text") or product.get("ingredients_text_en"))
        allergens = split_list(product.get("allergens") or product.get("allergens_from_ingredients"))
        claims = split_list(product.get("labels") or product.get("labels_tags"))

        return ProductLabel(
            name=first_text(product.get("product_name"), product.get("product_name_en"), default="Unnamed product"),
            brand=first_text(product.get("brands"), default=None),
            quantity=first_text(product.get("quantity"), default=None),
            barcode=first_text(product.get("code"), default=None),
            ingredients=ingredients,
            allergens=allergens,
            claims=claims,
            nutrition=NutritionFacts(
                serving_size=first_text(product.get("serving_size"), product.get("quantity"), default=None),
                calories=first_number(nutriments.get("energy-kcal_serving"), nutriments.get("energy-kcal_100g"), nutriments.get("energy-kcal")),
                fat_g=first_number(nutriments.get("fat_serving"), nutriments.get("fat_100g")),
                saturated_fat_g=first_number(nutriments.get("saturated-fat_serving"), nutriments.get("saturated-fat_100g")),
                carbohydrates_g=first_number(nutriments.get("carbohydrates_serving"), nutriments.get("carbohydrates_100g")),
                sugars_g=first_number(nutriments.get("sugars_serving"), nutriments.get("sugars_100g")),
                fiber_g=first_number(nutriments.get("fiber_serving"), nutriments.get("fiber_100g")),
                protein_g=first_number(nutriments.get("proteins_serving"), nutriments.get("proteins_100g")),
                sodium_mg=sodium_to_mg(first_number(nutriments.get("sodium_serving"), nutriments.get("sodium_100g"))),
            ),
            confidence=0.82,
            source="Open Food Facts",
            raw={"open_food_facts_url": product.get("url")},
        )


def normalize_query(query: str | None) -> str:
    value = (query or "").strip().lower()
    value = re.sub(r"\s+", " ", value)
    value = re.sub(r"^(a|an|the)\s+", "", value)
    return value


def split_list(value: Any) -> list[str]:
    if isinstance(value, list):
        return [str(item).replace("en:", "").strip() for item in value if str(item).strip()][:12]
    if not isinstance(value, str) or not value.strip():
        return []
    parts = re.split(r"[,;]", value)
    return [part.replace("en:", "").strip() for part in parts if part.strip()][:12]


def first_text(*values: Any, default: str | None = "") -> str | None:
    for value in values:
        if isinstance(value, str) and value.strip():
            return value.strip()
    return default


def first_number(*values: Any) -> float | None:
    for value in values:
        parsed = to_float(value)
        if parsed is not None:
            return parsed
    return None


def to_float(value: Any) -> float | None:
    if value is None:
        return None
    try:
        return round(float(value), 2)
    except (TypeError, ValueError):
        return None


def sodium_to_mg(value: float | None) -> float | None:
    if value is None:
        return None
    return round(value * 1000, 2)

