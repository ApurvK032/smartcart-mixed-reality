# SmartCart MR — Mixed Reality Assisted Grocery Pre-Billing

An MR-assisted grocery pre-billing system that lets customers scan products with a headset/smart-glasses camera while shopping, so their cart is already billed by the time they reach the counter. Combines classical barcode scanning with a lightweight vision-language model (VLM) fallback for barcode-free items like produce.

---

## Folder Structure

```
SmartCart-MR/
├── unity-mr-app/              # Unity project — runs on Meta Quest 3
│   ├── Assets/                #   Scenes, scripts, MR UI, prefabs
│   └── ProjectSettings/       #   Unity project config
│
├── inference-server/          # Python backend — runs on dev laptop
│   ├── api/                   #   FastAPI routes (scan, cart, label-qa)
│   ├── modules/
│   │   ├── barcode/           #     Barcode detection + decoding (pyzbar/ZXing)
│   │   ├── vlm/               #     Local VLM inference (Ollama + Moondream)
│   │   ├── catalog_matcher/   #     CLIP embedding match for produce / no-barcode
│   │   ├── label_qa/          #     Gemini Flash client for label questions
│   │   └── cart/              #     Cart state, totals, checkout summary
│   └── config/                #   Env, model paths, API keys
│
├── database/                  # Product database
│   ├── schema/                #   SQLite table definitions
│   └── seed/                  #   Sample product data (~50 items)
│
├── product-catalog/           # Reference assets for visual matching
│   ├── images/                #   Catalog images (used for CLIP embeddings)
│   └── embeddings/            #   Precomputed CLIP vectors
│
├── docs/                      # Documentation
│   ├── architecture/          #   System design notes
│   ├── diagrams/              #   Data-flow, sequence, module diagrams
│   └── report/                #   Final project report
│
├── scripts/                   # Utility scripts (seed DB, embed catalog, eval)
│
├── tests/                     # Test assets + evaluation
│   ├── sample-frames/         #   Captured camera frames for offline testing
│   ├── sample-barcodes/       #   Barcode images for decoder tests
│   └── evaluation/            #   Accuracy/latency benchmark results
│
├── MR_Smart_Pre_Billing_PID.md   # Project Initiation Document
└── README.md
```

---

## System Architecture

Two-tier system: a thin MR client on the Quest, and a Python inference server on a laptop. They communicate over local Wi-Fi using HTTP/WebSocket.

```
┌───────────────────────────────┐         ┌──────────────────────────────────┐
│   Meta Quest 3 (Unity app)    │         │   Laptop — Inference Server      │
│                               │         │                                  │
│   ┌───────────────────────┐   │         │   ┌──────────────────────────┐   │
│   │ Passthrough Camera    │   │         │   │ FastAPI                  │   │
│   │   (frame capture)     │   │         │   │   POST /scan             │   │
│   └──────────┬────────────┘   │         │   │   GET  /cart             │   │
│              │                │ Wi-Fi   │   │   POST /label-qa         │   │
│              ▼                │ HTTP    │   └────────────┬─────────────┘   │
│   ┌───────────────────────┐   │◀───────▶│                │                 │
│   │ Network Client        │   │         │                ▼                 │
│   └──────────┬────────────┘   │         │   ┌────────────────────────┐     │
│              ▼                │         │   │ Scan Router            │     │
│   ┌───────────────────────┐   │         │   │  barcode visible? ──┐  │     │
│   │ MR UI Layer           │   │         │   └──────┬──────────────┘  │     │
│   │  - product card       │   │         │          │ YES        NO   │     │
│   │  - cart panel         │   │         │          ▼            ▼    │     │
│   │  - confirm button     │   │         │   ┌──────────┐  ┌──────────┴──┐  │
│   │  - total overlay      │   │         │   │ Barcode  │  │ CLIP match  │  │
│   └───────────────────────┘   │         │   │ decoder  │  │   OR VLM    │  │
│                               │         │   └────┬─────┘  └──────┬──────┘  │
│                               │         │        └────────┬──────┘         │
│                               │         │                 ▼                │
│                               │         │   ┌────────────────────────┐     │
│                               │         │   │ Product DB (SQLite)    │     │
│                               │         │   └────────────┬───────────┘     │
│                               │         │                ▼                 │
│                               │         │   ┌────────────────────────┐     │
│                               │         │   │ Cart State Manager     │     │
│                               │         │   └────────────────────────┘     │
└───────────────────────────────┘         └──────────────────────────────────┘
```

---

## Pipeline — Item Scan Workflow

1. **Frame capture.** Unity grabs a frame from the Quest passthrough camera (or a controller-triggered still).
2. **Upload.** The frame is POSTed as a JPEG to the inference server's `/scan` endpoint.
3. **Barcode attempt (fast path).** The barcode module tries to locate and decode a 1D/2D code using pyzbar/ZXing. Typical latency: tens of ms.
4. **Fallback routing.** If no barcode is found or confidence is low, the frame is routed to the visual matcher:
   - **CLIP catalog match** — embed the frame, nearest-neighbor search against precomputed catalog embeddings. Fast, works well for the ~50-item demo catalog.
   - **VLM fallback** — if CLIP confidence is also low, the frame goes to a local Moondream 2 (via Ollama) with a prompt like *"What grocery item is shown? Answer with a single product name."*
5. **Database lookup.** The resolved product ID is used to fetch name, price, offers, and nutrition data from SQLite.
6. **Response.** Server returns a JSON product card:
   ```json
   { "product_id": "...", "name": "...", "price": 45.0, "source": "barcode|clip|vlm", "confidence": 0.92 }
   ```
7. **MR display.** Unity renders the product card as a world-space panel in the user's view with a Confirm / Cancel control.
8. **Cart update.** On confirm, Unity calls `/cart/add`; the server updates cart state and returns the new running total.

## Pipeline — Label Q&A Workflow (optional feature)

1. User points at a product label and triggers a voice/button query ("Is this gluten free?").
2. Unity captures the frame + question, POSTs to `/label-qa`.
3. Server forwards frame + prompt to Gemini 2.5 Flash (free tier).
4. Structured answer returned and spoken/displayed in MR view.

## Pipeline — Checkout Workflow

1. At the billing counter, cashier scans a QR code displayed in the user's MR view (cart handoff token).
2. Counter POS fetches `/cart/{token}` from the server — itemized list + total.
3. Cashier verifies physical cart vs. virtual cart, processes payment via existing POS.

---

## Technology Stack

| Layer | Tech |
|---|---|
| MR client | Unity 2022 LTS, Meta XR SDK, OpenXR |
| MR language | C# |
| Inference server | Python 3.11, FastAPI, Uvicorn |
| Barcode | pyzbar (libzbar) / ZXing.Net |
| Visual matching | OpenAI CLIP (ViT-B/32) via `open_clip` |
| VLM (local) | Moondream 2 via Ollama |
| VLM (cloud, optional) | Gemini 2.5 Flash (free tier) |
| Database | SQLite |
| Transport | HTTP (JSON + base64 JPEG) or WebSocket |

---

## Development Phases

1. **Phase 1 — Backend skeleton.** FastAPI server, barcode decode endpoint, SQLite product DB, seed ~50 items.
2. **Phase 2 — Unity client (desktop).** Prove the pipeline end-to-end with a webcam in the Unity editor before touching the Quest.
3. **Phase 3 — CLIP fallback.** Build the catalog, precompute embeddings, wire up the router.
4. **Phase 4 — Quest deployment.** Port to Meta XR SDK, passthrough camera access, MR UI panels.
5. **Phase 5 — VLM + Label Q&A.** Add Moondream fallback and Gemini label-Q&A feature.
6. **Phase 6 — Checkout handoff + evaluation.** QR-based cart handoff, accuracy/latency benchmarks, final report.

---

## Evaluation Metrics (to be captured in `tests/evaluation/`)

- Barcode decode success rate under store lighting
- CLIP top-1 accuracy on the catalog
- VLM accuracy on produce items
- End-to-end scan→display latency (target: < 1.5s)
- Full cart pre-billing time vs. traditional counter scanning
```