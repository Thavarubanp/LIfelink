# LifeLink Inventory Management Agent

This agent analyses hospital blood stock and recommends action. It finds shortages below threshold, packets
close to expiry, hospitals that can help in an emergency, and possible transfers. It runs on port 8003 and is
called by the Supervisor with the `X-Internal-Key` header.

**The agent has no side effects.** It never changes inventory and never sends notifications itself. The backend
supplies the stock snapshot. The Supervisor passes the recommendations to the Notification agent. The backend saves
the resulting alerts. Stock moves only through recorded donations and hospital-approved transfers in the backend.

## Analysis: `POST /analyze`

| `mode` | Used for | Input | Output (`recommendations`) |
|---|---|---|---|
| `monitor` (default) | `InventoryCheck`, run by the backend every `InventoryMonitoring:IntervalMinutes` | `inventories`: rows with `facility_id`, `facility_name`, `blood_group`, `current_units`, `minimum_threshold`, `expiring_soon_units`, `expiry_alert_days`. `public_requests`: open requests | `InventoryShortage` (below threshold, with hospitals that have spare stock) and `PacketsExpiringSoon` (packets inside the hospital's alert window, with hospitals or public requests that could use them) |
| `emergency` | `EmergencyShortage` from the Emergency Center | `emergency`: requesting hospital, group, units, priority. `stock_hospitals`: hospitals with compatible unexpired stock | `EmergencyStock`, one per hospital that can help, ranked by how much it can give |

How matching works:

- Shortage sources are other hospitals holding spare stock of the same group (above their own threshold).
- Expiring packets are matched to hospitals below threshold for that group. They are also matched to open public
  requests the packets are red-cell compatible with (for example, O- packets for an A+ request).
- In an emergency, the backend has already selected hospitals with compatible, unexpired stock. The agent ranks
  them.

## Why `/analyze` is not its own LangGraph graph

`/analyze` runs plain rule functions. This was a deliberate choice, checked against the project requirements on
2026-09-25:

- **The agentic layer is the Supervisor.** Its LangGraph graph routes each event, keeps the workflow state and
  decides which agent runs next. `/analyze` is one worker step in that graph: `InventoryCheck` runs inventory, then
  planning, then notification; `EmergencyShortage` runs planning, then inventory, then notification.
- **The analysis is one deterministic pass.** It compares stock with thresholds, checks expiry windows and blood
  compatibility, and ranks hospitals by spare units. There is no branching or LLM decision, so a graph around it
  would add a layer without changing the result.
- **What the requirements say.** The expansion spec requires a multi-agent architecture in which a Supervisor
  coordinates the workers. It also requires reuse of the existing LangChain/LangGraph structure for RAG and the
  chatbot. The SE3090 scope notes ask for no unnecessary services or endpoints and for reuse of existing services.
  None of these requires each worker's internal logic to be a graph.
- **What was not available.** The SE3090 marking criteria are not in the repository. If they require each
  student's agent to be a LangGraph workflow, the change is small: make `monitor` and `emergency` the nodes of a
  graph behind `/analyze`.

The original LangGraph workflow is kept for standalone use (see below).

## Endpoints

| Method | Path | Purpose |
|---|---|---|
| GET | `/health` | Health check (no key needed) |
| POST | `/analyze` | Supervisor entry point (above) |
| POST | `/run` | Legacy workflow: reads inventory from the backend itself and posts recommendations to `/api/notifications/recommendations` |

## Legacy scheduler

The original workflow (fetch, then shortages, surpluses, match, generate, send) is kept for standalone use. Its
polling loop is **off by default** (`SCHEDULER_ENABLED=false`). With the backend running, monitoring already happens
through the Supervisor, so turning the loop on would produce duplicate alerts.

## Configuration (`.env`)

| Variable | Default | Purpose |
|---|---|---|
| `INTERNAL_SERVICE_API_KEY` | `LifeLink-Internal-Agent-Key-2026` | Must match the backend and the other agents. Local-development default only; shared and production environments must override it (see the root README) |
| `SCHEDULER_ENABLED` | `false` | Legacy polling loop |
| `SCHEDULE_INTERVAL_MINUTES` | `30` | Legacy polling interval |
| `BACKEND_API_URL`, `INVENTORY_ENDPOINT`, `NOTIFICATION_ENDPOINT`, `REQUEST_TIMEOUT` | see `.env.example` | Legacy workflow only |

## Run

```bash
cd Agents/InventoryManagement
pip install -r requirements.txt
cp .env.example .env
uvicorn api.app:app --host 127.0.0.1 --port 8003
```

## Tests

```bash
pytest -q
```

## Layout

```
api/app.py                     endpoints and the optional legacy scheduler
services/analysis_service.py   monitor and emergency analysis (used by /analyze)
services/*_service.py          legacy workflow steps (fetch, match, recommend, send)
graph/workflow.py              legacy LangGraph workflow
config/, models/               settings, constants and data models
```
