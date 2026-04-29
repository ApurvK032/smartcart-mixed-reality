from __future__ import annotations

import io
from pathlib import Path
import sys

from fastapi.testclient import TestClient
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from app.main import app  # noqa: E402


client = TestClient(app)


def tiny_jpeg() -> bytes:
    image = Image.new("RGB", (8, 8), color=(230, 240, 245))
    output = io.BytesIO()
    image.save(output, format="JPEG")
    return output.getvalue()


def test_health() -> None:
    response = client.get("/health")
    assert response.status_code == 200
    payload = response.json()
    assert payload["ok"] is True
    assert payload["mode"]


def test_analyze_label_mock() -> None:
    response = client.post(
        "/analyze-label",
        files={"image": ("label.jpg", tiny_jpeg(), "image/jpeg")},
    )
    assert response.status_code == 200
    payload = response.json()
    assert payload["ok"] is True
    assert payload["product"]["name"]
    assert payload["summary"]


def test_compare_uses_free_general_fallback() -> None:
    response = client.post(
        "/compare",
        json={
            "question": "compare calories with a banana",
            "other_product": "banana",
            "current_product": {
                "name": "Demo cereal",
                "brand": "SmartCart",
                "nutrition": {"calories": 160, "sugars_g": 12, "protein_g": 4, "sodium_mg": 190},
                "confidence": 0.8,
                "source": "test",
            },
        },
    )
    assert response.status_code == 200
    payload = response.json()
    assert payload["ok"] is True
    assert "banana" in payload["answer"].lower()
    assert payload["metrics"][0]["name"] == "calories"
