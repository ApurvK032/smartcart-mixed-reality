# SmartCart Local AI Server

This server runs on the laptop and keeps the full pipeline free: local/open-source label reading, Open Food Facts lookup, and optional USDA FoodData Central lookup with a free API key.

## Quick Start

```powershell
cd inference-server
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
$env:SMARTCART_AI_MODE = "mock"
uvicorn app.main:app --host 0.0.0.0 --port 8000
```

Use `mock` first to verify Quest-to-laptop networking. For real label reading, use either:

```powershell
$env:SMARTCART_AI_MODE = "ollama"
$env:SMARTCART_OLLAMA_MODEL = "llava:7b"
```

or install `requirements-vlm.txt` and run:

```powershell
$env:SMARTCART_AI_MODE = "qwen"
$env:SMARTCART_QWEN_MODEL = "Qwen/Qwen3-VL-2B-Instruct"
```

The direct Qwen path is fully local but downloads a large model the first time.

## API

- `GET /health` confirms server mode.
- `POST /analyze-label` accepts multipart `image`.
- `POST /compare` compares current label nutrition to another food/product.
- `POST /ask` answers simple product questions or routes comparison questions.

The Quest app defaults to laptop hotspot IP `http://192.168.137.1:8000`.

Every label request is logged locally:

- `logs/last_frame.jpg`
- `logs/last_response.json`
- `logs/requests.log`
- rolling captures in `logs/captures/`
