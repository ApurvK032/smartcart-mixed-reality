from __future__ import annotations

import re

from app.schemas import AskResponse, CompareRequest, ComparisonMetric, ComparisonResponse, ProductLabel
from app.services.label_analyzer import build_summary
from app.services.nutrition_lookup import NutritionLookup


class SmartCartAssistant:
    def __init__(self, lookup: NutritionLookup):
        self.lookup = lookup

    async def compare(self, request: CompareRequest) -> ComparisonResponse:
        warnings: list[str] = []
        target_query = request.other_product or extract_comparison_target(request.question)
        other = None

        if target_query:
            try:
                other = await self.lookup.search(target_query)
            except Exception as exc:
                warnings.append(f"Internet comparison lookup failed: {exc}")

        if other is None and target_query:
            warnings.append(f"Could not find nutrition data for {target_query!r}.")

        metrics = build_metrics(request.current_product, other)
        answer = comparison_answer(request.current_product, other, metrics, target_query)
        source = other.source if other is not None else "No comparison source"
        return ComparisonResponse(
            current_product=request.current_product,
            other_product=other,
            metrics=metrics,
            answer=answer,
            source=source,
            warnings=warnings,
        )

    async def ask(self, question: str, current_product: ProductLabel | None) -> AskResponse:
        lowered = question.lower()
        if current_product is not None and ("compare" in lowered or "calorie" in lowered or "than" in lowered):
            comparison = await self.compare(CompareRequest(question=question, current_product=current_product))
            return AskResponse(answer=comparison.answer, product=current_product, comparison=comparison, warnings=comparison.warnings)

        if current_product is None:
            return AskResponse(
                answer="Scan a product label first, then ask about ingredients, allergens, calories, or comparisons.",
                warnings=["No current product was provided."],
            )

        if "allergen" in lowered or "allergy" in lowered:
            if current_product.allergens:
                answer = "Visible allergens: " + ", ".join(current_product.allergens)
            else:
                answer = "I do not see allergens in the current label data."
            return AskResponse(answer=answer, product=current_product)

        if "ingredient" in lowered:
            if current_product.ingredients:
                answer = "Ingredients I can read: " + ", ".join(current_product.ingredients[:12])
            else:
                answer = "I do not have readable ingredients for this product yet."
            return AskResponse(answer=answer, product=current_product)

        if "calorie" in lowered:
            calories = current_product.nutrition.calories
            answer = "Calories are not visible yet." if calories is None else f"This product has about {calories:g} kcal per listed serving or 100 g."
            return AskResponse(answer=answer, product=current_product)

        return AskResponse(answer=build_summary(current_product), product=current_product)


def extract_comparison_target(question: str) -> str | None:
    text = question.strip().lower()
    patterns = [
        r"(?:with|to|than)\s+(?:a|an|the)?\s*([a-z0-9][a-z0-9 \-]{1,60})",
        r"calories\s+(?:in|of)\s+(?:a|an|the)?\s*([a-z0-9][a-z0-9 \-]{1,60})",
    ]
    for pattern in patterns:
        match = re.search(pattern, text)
        if match:
            target = re.sub(r"\b(product|item|food|calories|nutrition)\b", "", match.group(1))
            target = re.sub(r"\s+", " ", target).strip(" .?")
            if target:
                return target
    return None


def build_metrics(current: ProductLabel, other: ProductLabel | None) -> list[ComparisonMetric]:
    metric_defs = [
        ("calories", current.nutrition.calories, other.nutrition.calories if other else None, "kcal"),
        ("sugar", current.nutrition.sugars_g, other.nutrition.sugars_g if other else None, "g"),
        ("protein", current.nutrition.protein_g, other.nutrition.protein_g if other else None, "g"),
        ("sodium", current.nutrition.sodium_mg, other.nutrition.sodium_mg if other else None, "mg"),
    ]
    metrics: list[ComparisonMetric] = []
    for name, current_value, other_value, unit in metric_defs:
        delta = None if current_value is None or other_value is None else round(current_value - other_value, 2)
        if current_value is not None or other_value is not None:
            metrics.append(ComparisonMetric(name=name, current_value=current_value, other_value=other_value, unit=unit, delta=delta))
    return metrics


def comparison_answer(current: ProductLabel, other: ProductLabel | None, metrics: list[ComparisonMetric], target_query: str | None) -> str:
    if other is None:
        name = target_query or "the comparison product"
        return f"I could not find enough free nutrition data for {name}. Try a more specific product name."

    calorie_metric = next((metric for metric in metrics if metric.name == "calories"), None)
    if calorie_metric and calorie_metric.current_value is not None and calorie_metric.other_value is not None:
        delta = calorie_metric.delta or 0
        if abs(delta) < 1:
            relation = "about the same calories as"
        elif delta > 0:
            relation = f"about {abs(delta):g} kcal more than"
        else:
            relation = f"about {abs(delta):g} kcal fewer than"
        return f"{current.name} has {relation} {other.name} ({calorie_metric.current_value:g} vs {calorie_metric.other_value:g} kcal)."

    return f"I found {other.name}, but one of the calorie values is missing. Available comparison fields: {', '.join(metric.name for metric in metrics) or 'none'}."

