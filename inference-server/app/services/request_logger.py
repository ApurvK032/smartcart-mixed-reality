from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from app.config import Settings
from app.schemas import AnalyzeLabelResponse


class RequestLogger:
    """Small file logger for debugging what the headset sends to the laptop."""

    def __init__(self, settings: Settings):
        self.settings = settings
        self.log_dir = Path(settings.log_dir)
        self.capture_dir = self.log_dir / "captures"

    def log_label_request(
        self,
        *,
        image_bytes: bytes,
        response: AnalyzeLabelResponse,
        client_host: str | None,
    ) -> None:
        self.log_dir.mkdir(parents=True, exist_ok=True)

        timestamp = datetime.now(timezone.utc)
        stem = timestamp.strftime("%Y%m%dT%H%M%S_%fZ")
        response_payload = response.model_dump(mode="json")

        if self.settings.save_frames:
            self.capture_dir.mkdir(parents=True, exist_ok=True)
            capture_path = self.capture_dir / f"{stem}_label.jpg"
            capture_path.write_bytes(image_bytes)
            (self.log_dir / "last_frame.jpg").write_bytes(image_bytes)
            self._prune_old_captures()
        else:
            capture_path = None

        last_response = {
            "timestamp_utc": timestamp.isoformat(),
            "client_host": client_host,
            "image_bytes": len(image_bytes),
            "capture_path": str(capture_path) if capture_path else None,
            "response": response_payload,
        }
        (self.log_dir / "last_response.json").write_text(json.dumps(last_response, indent=2), encoding="utf-8")

        line = {
            "timestamp_utc": timestamp.isoformat(),
            "client_host": client_host,
            "image_bytes": len(image_bytes),
            "mode": response.mode,
            "product_name": response.product.name,
            "summary": response.summary,
            "warnings": response.warnings,
        }
        with (self.log_dir / "requests.log").open("a", encoding="utf-8") as handle:
            handle.write(json.dumps(line, ensure_ascii=True) + "\n")

    def _prune_old_captures(self) -> None:
        max_frames = max(1, self.settings.max_logged_frames)
        captures = sorted(self.capture_dir.glob("*_label.jpg"), key=lambda path: path.stat().st_mtime, reverse=True)
        for old_capture in captures[max_frames:]:
            try:
                old_capture.unlink()
            except OSError:
                pass
