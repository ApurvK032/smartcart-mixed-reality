# SmartCart AI Pipeline

This is the end-to-end path currently built in the repo.

## Flow

1. Quest 3 runs the Unity app in `SmartCart/`.
2. The app requests headset camera permission.
3. A small rounded result panel sits in the top-left of view.
4. A rounded white capture box sits in the right-middle of view.
5. Every few seconds, Unity crops the headset camera frame to that right-middle label region.
6. The cropped JPEG is sent to the laptop server at `/analyze-label`.
7. The laptop server logs the exact crop and response.
8. If the server responds successfully, the capture box flashes green for one second, then returns to white.
9. Unity renders the returned product summary, nutrition, ingredients, or allergens on the small world-space panel.

## Free Modes

`SMARTCART_AI_MODE=mock` is free and lightweight. It proves that Quest-to-laptop networking, image upload, JSON parsing, and panel rendering work.

`SMARTCART_AI_MODE=ollama` is free and local once the Ollama model is installed. The default configured model name is `llava:7b`, but any Ollama vision model that accepts images can be used.

`SMARTCART_AI_MODE=qwen` is free and local using Hugging Face Transformers with `Qwen/Qwen3-VL-2B-Instruct`. It downloads a large model the first time and may need a capable GPU for comfortable latency.

## Verification Checklist

- `python -m compileall app tests`
- `python -m pytest tests -q`
- `GET /health` returns `ok: true`
- `POST /analyze-label` with an image returns product JSON
- `inference-server/logs/last_frame.jpg` updates after a label request
- `inference-server/logs/last_response.json` updates after a label request
- Unity batch build produces `SmartCart/Builds/SmartCart.apk`
- `adb install -r -d SmartCart/Builds/SmartCart.apk` succeeds
- `adb shell am start -n com.apurv.smartcart/com.unity3d.player.UnityPlayerGameActivity` starts the Quest app

## Current Limitations

- The current installed app focuses only on label capture and laptop logging. Barcode scanning is disabled.
- `SMARTCART_AI_MODE=mock` confirms that image upload and logging work, but it does not truly read labels.
- Voice capture from the Quest microphone is not implemented yet.
- The local fallback foods are approximate general nutrition examples for later comparisons.
