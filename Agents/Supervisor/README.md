# LifeLink Supervisor Agent

The Supervisor replaced the Planning Agent. It runs on the same port (8004) and keeps the `/plan` contract, and the
backend still finds it through `PlanningAgent:BaseUrl`. It routes platform events and assistant messages to the
three worker agents. Planning (prioritisation and next steps) and the knowledge base (RAG) run inside the Supervisor
as graph steps, not as separate services.

```
Browser --JWT--> Backend (ASP.NET Core, :5231) --X-Internal-Key--> Supervisor (:8004)
                                                                        |
                    +---------------------------------------------------+-------------------------------+
                    v                                                   v                               v
      Request Management (:8001)                             Notification (:8000)             Inventory (:8003)
      screening interview, versioned reports                 donor and hospital alerts        shortage, expiry, transfer advice
```

## Rules the Supervisor follows

- **The backend is the only writer.** Agents never write to the LifeLink database. `/plan` returns composed
  notifications, and the backend saves only those addressed to recipients the backend selected itself.
- **Role-scoped data only.** `/chat` receives a snapshot the backend built for the signed-in user's role. The
  Supervisor never queries user data on its own.
- **Guardrails on every reply.** Emails, phone numbers and IDs that are not in the user's own snapshot are removed.
  Claims such as "I approved you" are rewritten, and medical answers state that the doctor makes the final decision.
- **AI cannot approve, reject or verify.** Agents guide, recommend, explain, classify and write reports. Donor and
  donation decisions belong to the hospital's doctors and authorised staff.

## Graph

```
ingest -> supervisor --(next step)--> planning | knowledge | request_management | notification | inventory
             ^                                                 |
             +------------------ every step returns -----------+
supervisor --(queue empty or MAX_SUPERVISOR_STEPS reached)--> compose -> guard -> END
```

`ingest` turns an event or chat message into a queue of steps. Steps can add follow-up steps: for example, a
question asked mid-interview is answered from the knowledge base before the interview question is repeated.

## Events: `POST /plan`

| Event | Sent by (backend) | Steps | Result |
|---|---|---|---|
| `BloodRequestApproved` | `VerificationService`, when a doctor verifies a request | planning, notification | `EligibleDonorAlert` for donors that pass every check (compatible group, 120 days since last donation, age 18-60, active, not suspended or blocked). `UrgentHospitalAlert` for hospitals. |
| `DonorAccepted` | `AcceptanceService` | request_management | Starts the donor's screening interview. |
| `EmergencyShortage` | `EmergencyRequestService` | planning, inventory, notification | `EmergencyStockAlert` for hospitals holding compatible stock. |
| `InventoryCheck` | `InventoryMonitor`, every `InventoryMonitoring:IntervalMinutes` | inventory, planning, notification | `InventoryShortage` and `PacketsExpiringSoon` alerts, at most 3 per hospital. |

If the Supervisor is unreachable, the backend falls back to its own rule-based alerts.

## Chat: `POST /chat`

The backend calls this from `POST /api/assistant/chat`, which is rate-limited to 20 messages per minute per user.

- `mode: "assistant"`: intents are detected from rules, refined by Gemini when a key is set.
  - `account` runs planning, which summarises the user's own snapshot and suggests next steps.
  - `platform` and `medical` run the knowledge step.
- `mode: "screening"`: relays the donor's answer to the Request Management agent and returns the next question
  and the interview progress.

Each reply is split into segments labelled by their source:

| Segment | Content |
|---|---|
| `account` | The user's own data |
| `platform` | The LifeLink guide |
| `medical` | Blood donation guidance, always with source references |
| `screening` | The screening interview |

Suggested actions are links that the UI opens in the user's own session. Agents never act on the user's behalf.

## Knowledge base (RAG)

- Two separate ChromaDB collections: `knowledge/medical/` and `knowledge/platform/`. They are never searched
  together.
- Every medical document must declare `publisher` and `source_url` in its front matter. Documents without them are
  skipped. Example:

  ```
  ---
  title: WHO guidelines on assessing donor suitability
  publisher: World Health Organization
  source_url: https://www.who.int/publications/i/item/9789241548519
  year: 2012
  ---
  # Section heading
  Text...
  ```

- Embeddings use `models/gemini-embedding-001`. Vectors are stored in `.chroma/`, which is git-ignored.
- On startup, only new or changed chunks are embedded. To embed manually, run `python -m knowledge.ingest`; add
  `--rebuild` to start from scratch.
- A passage is used only if its cosine distance is at most `RETRIEVAL_MAX_DISTANCE` (0.40). This was measured on
  this knowledge base: related questions scored 0.18-0.34 and off-topic ones 0.45 or higher. If nothing is close
  enough, the assistant says it has no verified guidance. Re-measure the cut-off if the embedding model changes.
- Without a Gemini key, retrieval uses keyword scoring over the same documents and quotes the best passage.
- The medical documents are summaries written for this project. The team should review them against the cited
  sources before a demo.

## Configuration (`.env`)

| Variable | Default | Purpose |
|---|---|---|
| `HOST` / `PORT` | `127.0.0.1` / `8004` | Loopback only; the backend is the only caller |
| `INTERNAL_SERVICE_API_KEY` | `LifeLink-Internal-Agent-Key-2026` | Must match the backend's `InternalService:ApiKey` and every agent. Local-development default only; shared and production environments must override it (see the root README) |
| `AGENT1_SCREENING_URL` | `http://127.0.0.1:8001` | Request Management agent |
| `AGENT2_MATCHING_URL` | `http://127.0.0.1:8000` | Notification agent |
| `AGENT3_INVENTORY_URL` | `http://127.0.0.1:8003` | Inventory agent |
| `GOOGLE_API_KEY` (or `GEMINI_API_KEY`) | empty | Optional; enables Gemini answers, intent refinement and vector search |
| `MODEL_NAME` / `EMBEDDING_MODEL` | `gemini-2.5-flash` / `models/gemini-embedding-001` | |
| `RETRIEVAL_MAX_DISTANCE` | `0.40` | Knowledge relevance cut-off |
| `MAX_SUPERVISOR_STEPS` | `8` | Step budget per request |

## Run

```bash
cd Agents/Supervisor
pip install -r requirements.txt
cp .env.example .env          # add GOOGLE_API_KEY if available
python app.py                 # or: uvicorn app:app --host 127.0.0.1 --port 8004
```

Start the three worker agents too. `GET /health` shows each worker as `REACHABLE` or `UNREACHABLE`, and whether each
knowledge collection is using vectors. `/plan` and `/chat` require the `X-Internal-Key` header.

## Tests

```bash
pytest -q
```

The tests need no key or network: `tests/conftest.py` forces rule-based routing and keyword retrieval.

## Layout

```
api/          routes.py (/health, /plan, /chat), security.py (internal key check)
config/       settings.py
graph/        supervisor.py (graph), nodes.py (steps), state.py
knowledge/    store.py (ChromaDB + keyword fallback), ingest.py, medical/*.md, platform/*.md
models/       request.py, response.py
services/     agent_clients.py (worker HTTP calls), intents.py, planner.py, guardrails.py, llm.py, supervisor_service.py
tests/        test_events.py, test_chat.py, test_knowledge.py
```
