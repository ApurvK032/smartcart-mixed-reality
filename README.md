# SmartCart MR

SmartCart is a Meta Quest 3 mixed-reality grocery label reader. The headset app captures a label region from the right-middle of the user's view, sends that cropped image to a laptop-hosted FastAPI server, and shows the returned product information on a small translucent panel.

## What Is Implemented

- `SmartCart/`: Unity 6000.3 Quest project with OpenXR, Meta Quest support, AR Foundation camera access, passthrough-friendly scene setup, a right-side label capture frame, and laptop AI crop upload.
- `inference-server/`: FastAPI service with `/health`, `/analyze-label`, `/compare`, and `/ask`.
- Current focus: label image capture and laptop logging. Barcode scanning is disabled for now.
- Free product data path for future comparisons: Open Food Facts search, local general-food fallback, and optional USDA FoodData Central with a free key.
- Free VLM path: mock mode for networking tests, Ollama mode, or direct local Qwen mode. No paid API is required.

## Run The Laptop Server

```powershell
cd inference-server
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
$env:SMARTCART_AI_MODE = "mock"
uvicorn app.main:app --host 0.0.0.0 --port 8000
```

For real label reading, switch to a local open-source model:

```powershell
$env:SMARTCART_AI_MODE = "ollama"
$env:SMARTCART_OLLAMA_MODEL = "llava:7b"
```

or install the optional direct VLM stack:

```powershell
pip install -r requirements-vlm.txt
$env:SMARTCART_AI_MODE = "qwen"
$env:SMARTCART_QWEN_MODEL = "Qwen/Qwen3-VL-2B-Instruct"
```

The model files are free to use, but the first download is large and local inference speed depends on the laptop GPU/CPU.

## Quest Networking

The Unity app tries the Windows hotspot default first:

```text
http://192.168.137.1:8000
```

For USB testing without hotspot, run:

```powershell
adb reverse tcp:8000 tcp:8000
```

The app then falls back to:

```text
http://127.0.0.1:8000
```

## Build And Install

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Unity.exe" `
  -batchmode -quit `
  -projectPath "$PWD\SmartCart" `
  -executeMethod SmartCartProjectBuilder.ConfigureAndBuild `
  -logFile "$PWD\SmartCart\SmartCartBuild.log"

& "C:\Program Files\Unity\Hub\Editor\6000.3.6f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" install -r -d "SmartCart\Builds\SmartCart.apk"
```

The built app id is `com.apurv.smartcart`.

## API Examples

```powershell
Invoke-RestMethod http://127.0.0.1:8000/health
```

`POST /analyze-label` expects multipart field `image`.

`POST /compare` accepts a current product plus another food/product name, then searches free sources and returns a calorie/nutrition comparison.

## Laptop Logs

Every label crop received from the Quest is saved under:

```text
inference-server/logs/
```

Useful files:

- `last_frame.jpg`: latest label crop from the headset or test client
- `last_response.json`: latest model/API response
- `requests.log`: one JSON line per request
- `captures/`: rolling saved label crop images
