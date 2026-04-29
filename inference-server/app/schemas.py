from __future__ import annotations

from typing import Any

from pydantic import BaseModel, Field


class NutritionFacts(BaseModel):
    serving_size: str | None = None
    calories: float | None = None
    calories_unit: str = "kcal"
    fat_g: float | None = None
    saturated_fat_g: float | None = None
    carbohydrates_g: float | None = None
    sugars_g: float | None = None
    fiber_g: float | None = None
    protein_g: float | None = None
    sodium_mg: float | None = None


class ProductLabel(BaseModel):
    name: str = "Unknown product"
    brand: str | None = None
    quantity: str | None = None
    barcode: str | None = None
    ingredients: list[str] = Field(default_factory=list)
    allergens: list[str] = Field(default_factory=list)
    claims: list[str] = Field(default_factory=list)
    nutrition: NutritionFacts = Field(default_factory=NutritionFacts)
    confidence: float = 0.0
    source: str = "unknown"
    raw: dict[str, Any] = Field(default_factory=dict)


class AnalyzeLabelResponse(BaseModel):
    ok: bool = True
    mode: str
    barcode: str | None = None
    product: ProductLabel
    summary: str
    warnings: list[str] = Field(default_factory=list)


class CompareRequest(BaseModel):
    question: str = "compare calories"
    current_product: ProductLabel
    other_product: str | None = None


class ComparisonMetric(BaseModel):
    name: str
    current_value: float | None = None
    other_value: float | None = None
    unit: str = "kcal"
    delta: float | None = None


class ComparisonResponse(BaseModel):
    ok: bool = True
    current_product: ProductLabel
    other_product: ProductLabel | None = None
    metrics: list[ComparisonMetric] = Field(default_factory=list)
    answer: str
    source: str
    warnings: list[str] = Field(default_factory=list)


class AskRequest(BaseModel):
    question: str
    current_product: ProductLabel | None = None


class AskResponse(BaseModel):
    ok: bool = True
    answer: str
    product: ProductLabel | None = None
    comparison: ComparisonResponse | None = None
    warnings: list[str] = Field(default_factory=list)

