# LifeLink Request Management Agent (Donor Screening)

This agent runs the donor screening interview after a donor accepts a blood request. It records the answers and
submits a versioned screening report to the hospital's doctor through the backend. It runs on port 8001 and is
called only by the Supervisor (and the backend). Both calls carry the `X-Internal-Key` header.

**The agent never approves or rejects a donor.** It asks questions, explains terms, flags risks and writes the
report. The assigned doctor, or another active doctor at the same hospital, makes the decision in the backend
(`DonorVerifications`).

## Flow

1. The donor accepts a request. The backend sends `DonorAccepted` to the Supervisor, which calls
   `POST /api/agent/screening/start/{acceptanceId}`. The session opens and the backend marks the acceptance
   `ScreeningPending`.
2. The donor answers in the chat (`/donor/acceptances/{id}/screening` or the assistant widget). Each message is one
   turn, handled by `POST /api/agent/screening/turn/{acceptanceId}`:

   ```
   load -> interpret --answer--> record --(more questions)--> END
                                        +--(last answer)---> submit -> END
                    +--question--> explain  (the Supervisor answers from the medical knowledge base, then repeats the question)
                    +--clarify---> clarify
                    +--withdraw--> withdraw (points to the Withdraw button; the agent never withdraws for the donor)
   ```

3. After the last answer, the rules in `services/eligibility_rules.py` set the risk level and the AI
   recommendation (`Eligible`, `Temporarily Deferred` or `Requires Doctor Review`). Gemini writes a summary from
   de-identified answers. The report is submitted to the backend (`POST api/screening-agent/report-notify`), which
   stores it as a new immutable version and notifies the doctor.
4. If the donor chooses **Update my answers**, the backend moves the acceptance back to `ScreeningPending`. The
   next turn starts a new revision with the previous answers offered as defaults. Every submitted version is kept.

The interview only runs while the backend shows the acceptance as `Accepted` or `ScreeningPending`. Withdrawn,
decided, released or expired donations return a "screening is closed" reply.

## Questionnaire

There are 12 sections, defined in `models/donor_screening.py`:

1. Personal information
2. Previous donation history
3. Current health status
4. Basic eligibility
5. Medical history
6. Recent medical events
7. Recent diseases
8. Dental and medication history
9. Travel history
10. Infectious disease risk (confidential)
11. Recent symptoms
12. Female donors

How the questions behave:

- Section 1 is pre-filled from the donor's profile, and the donor confirms or corrects each value.
- Follow-up questions appear only when the parent answer triggers them. Female-only questions are skipped for
  other donors.
- Section 10 answers are parsed by fixed rules only. They are never sent to an LLM and never shown in the chat
  transcript.
- The LLM summary never receives Section 1 (personal details) or Section 10 answers. Reports use the schema
  `lifelink.screening.v1`.

## Endpoints (all under `/api/agent`)

| Method | Path | Purpose |
|---|---|---|
| GET | `/health` | Health, model name and whether Gemini is configured (no key needed) |
| POST | `/screening/start/{acceptanceId}` | Open or resume the interview. Returns 409 if screening is closed |
| GET | `/screening/session/{acceptanceId}` | Current question, progress and transcript, for resuming |
| POST | `/screening/turn/{acceptanceId}` | One donor message: `{"message": "..."}` |
| GET | `/report/{acceptanceId}` | Latest submitted report (the backend holds the official copy of every version) |

If the backend is unreachable, the agent returns 503 and saves nothing.

## Configuration (`.env`)

| Variable | Default | Purpose |
|---|---|---|
| `HOST` / `PORT` | `127.0.0.1` / `8001` | Loopback only |
| `DATABASE_URL` | `sqlite:///./screening_agent.db` | Agent-owned tables: sessions, answers, report versions |
| `GEMINI_API_KEY` (or `GOOGLE_API_KEY`) | empty | Optional; without it a rule-based summary is used |
| `MODEL_NAME` | `gemini-2.5-flash` | |
| `BACKEND_BASE_URL` | `http://127.0.0.1:5231` | LifeLink backend |
| `INTERNAL_SERVICE_API_KEY` | `LifeLink-Internal-Agent-Key-2026` | Must match the backend and the other agents. Local-development default only; shared and production environments must override it (see the root README) |

## Run

```bash
cd Agents/RequestManagement
pip install -r requirements.txt
cp .env.example .env
python main.py            # or: uvicorn app:app --host 127.0.0.1 --port 8001
```

On first start, a new database gets all tables automatically.

**Existing databases.** A `screening_agent.db` created before report versioning needs migration
`002_versioned_reports`. The migration:

- adds the session revision and the report version and JSON columns;
- drops the old `doctor_reviews` table;
- marks old-questionnaire sessions `Legacy`, so those donors restart on the new questions.

Run it with:

```bash
alembic upgrade head
# If the file was created by the app rather than Alembic, first run:
# alembic stamp 001_initial_screening_agent_tables
```

The screening data is development data, so deleting the file is also fine.

## Tests

```bash
pytest -q
```

The tests use an in-memory database and a stubbed backend. They need no key or network.
