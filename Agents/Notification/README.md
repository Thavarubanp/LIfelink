# LifeLink Notification Agent

This agent composes alerts for donors and hospitals. It runs on port 8000 and is called by the Supervisor (and
the backend) with the `X-Internal-Key` header. It never writes to the database. It returns notifications, and the
backend saves only those whose recipients the backend itself selected.

## What it does

**Donor alerts** (`POST /process-request`, used for `BloodRequestApproved`):

```
check priority -> find eligible donors (rules) -> rank (Gemini or deterministic) -> compose alerts
```

- **Eligibility uses fixed rules, never Gemini.** The backend sends only donors it already filtered. The agent
  re-checks every rule before alerting anyone:
  - active account, not suspended, not permanently blocked;
  - compatible blood group;
  - at least 120 days since the last donation;
  - age 18-60 when the date of birth is known.
- **Ranking** sends Gemini only the user ID, blood group and last donation date. Names, locations and contact
  details are never sent. Without a key, or if the call fails, a deterministic ranking is used.
- **Alert text** is written once per role (donor, hospital) from request facts only, with templates as the
  fallback. Hospital alerts are added only for High and Critical priority.

**Hospital alerts** (`POST /hospital-alerts`, used for `EmergencyShortage` and `InventoryCheck`):

- The facts come from the Inventory agent: which hospital, which blood group, how many units, and which hospitals
  are related.
- The agent turns them into alerts of these types:

  | Alert type | Meaning |
  |---|---|
  | `EmergencyStockAlert` | Emergency support needed |
  | `InventoryShortage` | Stock below threshold |
  | `PacketsExpiringSoon` | Packets close to expiry |
  | `TransferSuggestion` | A transfer could balance stock |

- Gemini may only reword the title and message. Templates are used otherwise.

## Endpoints

| Method | Path | Purpose |
|---|---|---|
| GET | `/health` | Health, model name, whether a key is configured (no internal key needed) |
| POST | `/process-request` | Donor (and High/Critical hospital) alerts for an approved request |
| POST | `/hospital-alerts` | Hospital alerts from Inventory agent facts: `{"alerts": [...], "context": {...}}` |
| POST | `/rank-donors`, `/generate-notifications` | The individual steps, for testing |

Example `/process-request` body (the backend builds this in `DonorCandidate.ToAgentPayload`):

```json
{
  "request_id": "3fa85f64-...", "blood_group": "O-", "units_required": 2, "priority": "Critical",
  "hospital_id": "e4b6...", "hospital_name": "City General",
  "available_donors": [
    {"user_id": "a1b2...", "full_name": "...", "blood_group": "O-", "location": "...",
     "last_donation_date": "2026-01-15", "date_of_birth": "1995-04-10",
     "account_status": "Active", "is_suspended": false, "is_blocked": false}
  ],
  "verified_hospital_ids": ["e4b6..."]
}
```

## Configuration (`.env`)

| Variable | Default | Purpose |
|---|---|---|
| `GOOGLE_API_KEY` (or `GEMINI_API_KEY`) | empty | Optional; enables Gemini ranking and wording |
| `MODEL_NAME` | `gemini-2.5-flash` | |
| `INTERNAL_SERVICE_API_KEY` | `LifeLink-Internal-Agent-Key-2026` | Must match the backend and the other agents. Local-development default only; shared and production environments must override it (see the root README) |
| `HOST` / `PORT` | `127.0.0.1` / `8000` | Loopback only |

## Run

```bash
cd Agents/Notification
pip install -r requirements.txt
cp .env.example .env
python app.py             # or: uvicorn app:app --host 127.0.0.1 --port 8000
```

## Tests

```bash
pytest -q
```
