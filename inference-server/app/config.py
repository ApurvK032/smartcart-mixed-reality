from __future__ import annotations

import os
from dataclasses import dataclass


@dataclass(frozen=True)
class Settings:
    """Environment-backed settings for the free local SmartCart pipeline."""

    ai_mode: str = os.getenv("SMARTCART_AI_MODE", "mock").strip().lower()
    host: str = os.getenv("SMARTCART_HOST", "0.0.0.0")
    port: int = int(os.getenv("SMARTCART_PORT", "8000"))
    request_timeout_seconds: float = float(os.getenv("SMARTCART_REQUEST_TIMEOUT", "20"))
    ollama_base_url: str = os.getenv("OLLAMA_BASE_URL", "http://127.0.0.1:11434").rstrip("/")
    ollama_model: str = os.getenv("SMARTCART_OLLAMA_MODEL", "llava:7b")
    qwen_model_id: str = os.getenv("SMARTCART_QWEN_MODEL", "Qwen/Qwen3-VL-2B-Instruct")
    usda_api_key: str = os.getenv("USDA_API_KEY", "").strip()
    max_upload_mb: int = int(os.getenv("SMARTCART_MAX_UPLOAD_MB", "8"))
    log_dir: str = os.getenv("SMARTCART_LOG_DIR", "logs")
    save_frames: bool = os.getenv("SMARTCART_SAVE_FRAMES", "true").strip().lower() not in {"0", "false", "no"}
    max_logged_frames: int = int(os.getenv("SMARTCART_MAX_LOGGED_FRAMES", "50"))

    @property
    def max_upload_bytes(self) -> int:
        return self.max_upload_mb * 1024 * 1024


def get_settings() -> Settings:
    return Settings()
