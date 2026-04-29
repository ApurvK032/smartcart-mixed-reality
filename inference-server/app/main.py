from __future__ import annotations

from fastapi import FastAPI, File, HTTPException, Request, UploadFile
from fastapi.middleware.cors import CORSMiddleware

from app.config import get_settings
from app.schemas import AnalyzeLabelResponse, AskRequest, AskResponse, CompareRequest, ComparisonResponse
from app.services.chat import SmartCartAssistant
from app.services.label_analyzer import LabelAnalyzer
from app.services.nutrition_lookup import NutritionLookup
from app.services.request_logger import RequestLogger


settings = get_settings()
lookup = NutritionLookup(settings)
analyzer = LabelAnalyzer(settings, lookup)
assistant = SmartCartAssistant(lookup)
request_logger = RequestLogger(settings)

app = FastAPI(
    title="SmartCart Local AI Server",
    version="0.1.0",
    description="Free local label analysis and product comparison API for the SmartCart Quest app.",
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=False,
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.get("/health")
async def health() -> dict[str, object]:
    return {
        "ok": True,
        "mode": settings.ai_mode,
        "vlm": {
            "ollama_model": settings.ollama_model,
            "qwen_model": settings.qwen_model_id,
        },
    }


@app.post("/analyze-label", response_model=AnalyzeLabelResponse)
async def analyze_label(
    request: Request,
    image: UploadFile = File(...),
) -> AnalyzeLabelResponse:
    image_bytes = await image.read()
    if not image_bytes:
        raise HTTPException(status_code=400, detail="No image bytes were uploaded.")
    if len(image_bytes) > settings.max_upload_bytes:
        raise HTTPException(status_code=413, detail=f"Image is larger than {settings.max_upload_mb} MB.")

    try:
        response = await analyzer.analyze(image_bytes)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc

    try:
        request_logger.log_label_request(
            image_bytes=image_bytes,
            response=response,
            client_host=request.client.host if request.client else None,
        )
    except OSError as exc:
        response.warnings.append(f"Could not write laptop debug log: {exc}")

    return response


@app.post("/compare", response_model=ComparisonResponse)
async def compare(request: CompareRequest) -> ComparisonResponse:
    return await assistant.compare(request)


@app.post("/ask", response_model=AskResponse)
async def ask(request: AskRequest) -> AskResponse:
    return await assistant.ask(request.question, request.current_product)
