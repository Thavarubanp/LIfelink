# LifeLink Student 1: AI-Powered Donor Screening Agent

Production-ready clinical AI microservice for the LifeLink Blood Donation System.
Orchestrates donor medical screening interviews, dynamic conditional branching, Google Gemini LLM clinical risk evaluations, and comprehensive doctor reports while strictly respecting clinical governance (doctors retain 100% final medical decision authority).

---

## Architecture & Isolation
- **Microservice Location:** `backend/Agents/RequestManagement/`
- **Port:** `8001` (completely isolated from Student 2 Notification Agent on port `8000`)
- **Integration:** Communicates with the ASP.NET Core Backend exclusively over REST APIs (`httpx`).
- **No Direct DB Coupling:** Uses its own dedicated database tables (`ScreeningSession`, `DonorScreeningAnswer`, `DonorScreeningReport`, `DoctorReview`).
- **Clinical Governance:** AI **NEVER** approves or rejects donors. Allowed AI recommendations:
  - `Eligible`
  - `Temporarily Deferred`
  - `Requires Doctor Review`
  Final approval/rejection is strictly performed by the licensed doctor.

---

## Features & Dynamic Questioning
- **No Profile Inquiries:** Does NOT prompt for Name, Email, Phone, NIC, or Address; automatically fetches demographic profile from backend REST APIs.
- **12 Health Sections:**
  1. General Health
  2. Donation History
  3. Medications *(Conditional: skips details if donor takes no medications)*
  4. Medical Conditions *(Conditional: skips details if donor has no chronic illness)*
  5. Infectious Diseases *(Conditional: skips details if no exposure)*
  6. Medical Procedures *(Conditional: skips details if no recent surgeries)*
  7. Vaccinations *(Conditional: skips details if no recent vaccine)*
  8. Travel History *(Conditional: skips details if no travel)*
  9. Lifestyle Risks *(Conditional: skips details if no exposure)*
  10. Pregnancy & Women's Health *(Asked strictly to female donors)*
  11. Allergies *(Conditional)*
  12. Consent (Truthfulness, testing consent, voluntary agreement)
- **LangGraph Workflow (11 Nodes):**
  `START` -> `Load Acceptance` -> `Create Session` -> `Ask Questions` -> `Collect Answers` -> `Validate Answers` -> `Gemini Analysis` -> `Risk Classification` -> `Generate Report` -> `Store Report` -> `Mark Completed` -> `Send To Doctor Review Queue` -> `END`
- **Dual Report Persistence:**
  1. Permanent database record in `donor_screening_reports`
  2. Permanent JSON export in `reports/generated/report_{acceptanceId}.json`
- **Doctor Physical Review:**
  Records vitals (weight, BP, pulse, temperature, hemoglobin) and decision (`Approved`, `Rejected`, `FurtherScreeningRequired`).

---

## Setup & Running Guide

### 1. Create Virtual Environment
```bash
cd backend/Agents/RequestManagement
python -m venv .venv

# On Windows
.venv\Scripts\activate

# On Linux/macOS
source .venv/bin/activate
```

### 2. Install Dependencies
```bash
pip install -r requirements.txt
```

### 3. Configure Environment Variables
Copy `.env.example` to `.env`:
```bash
cp .env.example .env
```
Key parameters in `.env`:
- `PORT=8001`
- `DATABASE_URL=sqlite:///./screening_agent.db` (or Neon PostgreSQL connection string)
- `GEMINI_API_KEY=your_gemini_api_key`
- `MODEL_NAME=gemini-2.5-flash`
- `BACKEND_BASE_URL=http://localhost:5231`

### 4. Run Migrations
```bash
alembic upgrade head
```

### 5. Start FastAPI Microservice
```bash
# Using Python
python main.py

# Or using Uvicorn
uvicorn app:app --host 0.0.0.0 --port 8001 --reload
```
Interactive Swagger API documentation will be available at:
`http://localhost:8001/docs`

---

## Test Suite Execution
Run the comprehensive test suite (all tests execute independently with zero network dependencies thanks to the embedded clinical heuristic engine):
```bash
pytest -v
```

---

## API Endpoints & Example Payloads

### 1. Health Check
`GET /api/agent/health`

### 2. Start Donor Screening Session
`POST /api/agent/screening/start/{acceptanceId}`
**Response:**
```json
{
  "session_id": "8fa17a44-2451-4fa3-b684-25cb48123abc",
  "acceptance_id": "acc-123",
  "donor_user_id": "usr-456",
  "blood_request_id": "req-789",
  "status": "InProgress",
  "total_estimated_questions": 15,
  "first_question": {
    "question_id": "GH_1",
    "section": "SECTION 1: General Health",
    "question_text": "Are you feeling well and in good health today?",
    "question_type": "yes_no"
  }
}
```

### 3. Submit Question Answer
`POST /api/agent/screening/answer/{acceptanceId}`
**Request:**
```json
{
  "question_id": "GH_1",
  "answer": "Yes"
}
```
**Response:**
```json
{
  "session_id": "8fa17a44-2451-4fa3-b684-25cb48123abc",
  "question_id": "GH_1",
  "status": "InProgress",
  "is_complete": false,
  "next_question": {
    "question_id": "GH_2",
    "section": "SECTION 1: General Health",
    "question_text": "Have you had any fever, chills, or night sweats in the past 4 weeks?",
    "question_type": "yes_no"
  }
}
```

### 4. Complete Screening
`POST /api/agent/screening/complete/{acceptanceId}`
**Response:**
```json
{
  "session_id": "8fa17a44-2451-4fa3-b684-25cb48123abc",
  "report_id": "rep-987",
  "acceptance_id": "acc-123",
  "status": "UnderDoctorReview",
  "risk_level": "LOW",
  "recommendation": "Eligible",
  "summary": "Standard donor screening interview completed with no disqualifying medical factors or high-risk flags reported.",
  "flags": [],
  "report_json_path": "reports/generated/report_acc-123.json"
}
```

### 5. Doctor Views Full Screening Report
`GET /api/agent/report/{acceptanceId}`
Returns complete sections A to J with 100% of donor answers, AI findings, and clinical flags.

### 6. Doctor Submits Physical Vitals & Final Decision
`POST /api/agent/review/{reportId}`
**Request:**
```json
{
  "doctor_user_id": "doc-uuid-111",
  "weight": 68.5,
  "blood_pressure": "120/80",
  "pulse_rate": 74,
  "temperature": 36.6,
  "hemoglobin": 14.1,
  "decision": "Approved",
  "doctor_notes": "All vital signs optimal. Donor physically verified and approved for donation."
}
```
**Response:**
```json
{
  "review_id": "rev-555",
  "report_id": "rep-987",
  "doctor_user_id": "doc-uuid-111",
  "weight": 68.5,
  "blood_pressure": "120/80",
  "pulse_rate": 74,
  "temperature": 36.6,
  "hemoglobin": 14.1,
  "decision": "Approved",
  "doctor_notes": "All vital signs optimal. Donor physically verified and approved for donation.",
  "reviewed_at": "2026-09-11T15:30:00Z"
}
```
