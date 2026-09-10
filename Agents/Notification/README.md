# LifeLink - Student 2 Donor Discovery & Notification Agent

Agentic AI microservice built with **LangGraph**, **FastAPI**, and **Google Gemini** for the LifeLink Blood Donation Management Platform.

---

## 🏗️ Architecture & Purpose

The **Notification Agent** is a dedicated Python microservice that orchestrates the following workflow upon blood request approval:

```
[Blood Request Approved by Hospital in ASP.NET Core]
                      ↓
  [POST /process-request (FastAPI Agent Microservice)]
                      ↓
            1. CheckPriorityNode (Urgency Evaluation)
                      ↓
            2. FindEligibleDonorsNode (Business Rules Filtering)
                      ↓
            3. RankDonorsNode (Gemini AI Candidate Ranking)
                      ↓
            4. GenerateNotificationsNode (Gemini AI Multichannel Copywriting)
                      ↓
  [Structured Notifications & Ranked Donors Returned to ASP.NET Core Backend]
                      ↓
[Notifications Persisted in PostgreSQL Database & Dispatched]
```

### Key Responsibilities:
- **Eligibility Filtering**: Uses strict medical/business logic (blood group compatibility matrix & active account status). Gemini does **not** decide eligibility.
- **Donor Ranking**: Uses Google Gemini (`gemini-2.5-flash`) to rank eligible donors based on exact blood match, geographic proximity, and donation interval.
- **Notification Generation**: Uses Gemini to craft personalized, compassionate notifications for eligible donors, and clinical/administrative alerts for verified hospitals and admins.

---

## 🚀 Setup & Installation Guide

### Prerequisites
- Python 3.10+ installed
- Google Gemini API Key ([Get one here](https://aistudio.google.com/))

### 1. Create and Activate Virtual Environment

**Windows (PowerShell):**
```powershell
cd backend/Agents/Notification
python -m venv .venv
.\.venv\Scripts\Activate.ps1
```

**Linux / macOS:**
```bash
cd backend/Agents/Notification
python3 -m venv .venv
source .venv/bin/activate
```

### 2. Install Dependencies

```bash
pip install -r requirements.txt
```

### 3. Configure Environment Variables

Create a `.env` file from `.env.example`:

```bash
cp .env.example .env
```

Edit `.env` and provide your Google Gemini API key:
```env
GOOGLE_API_KEY=your_gemini_api_key_here
MODEL_NAME=gemini-2.5-flash
PORT=8000
HOST=0.0.0.0
```

---

## 🏃 Running the Agent Service

Start the FastAPI server:

```bash
uvicorn app:app --host 0.0.0.0 --port 8000 --reload
```
or
```bash
python app:app
```

The service will be live at:
- **API Base URL**: `http://localhost:8000`
- **Interactive Swagger Docs**: `http://localhost:8000/docs`

---

## 📡 API Endpoints

### 1. Health Check
`GET /health`
```json
{
  "status": "Healthy",
  "agent": "LifeLink Donor Discovery & Notification Agent",
  "framework": "LangGraph + FastAPI",
  "model": "gemini-2.5-flash",
  "is_api_key_configured": true
}
```

### 2. Full LangGraph Process Workflow
`POST /process-request`

**Request Payload:**
```json
{
  "request_id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "blood_group": "O-",
  "units_required": 2,
  "priority": "Critical",
  "hospital_id": "e4b60000-0000-0000-0000-000000000001",
  "hospital_name": "City General Trauma Center",
  "patient_reason": "Emergency accident trauma surgery",
  "available_donors": [
    {
      "user_id": "a1b2c3d4-0000-0000-0000-000000000001",
      "full_name": "John Doe",
      "blood_group": "O-",
      "location": "North District (2km away)",
      "last_donation_date": "2025-10-15",
      "account_status": "Active"
    },
    {
      "user_id": "a1b2c3d4-0000-0000-0000-000000000002",
      "full_name": "Jane Smith",
      "blood_group": "A+",
      "location": "Central District",
      "account_status": "Active"
    }
  ],
  "verified_hospital_ids": ["e4b60000-0000-0000-0000-000000000002"]
}
```

**Response Payload:**
```json
{
  "request_id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "priority": "Critical",
  "is_urgent": true,
  "eligible_donors_count": 1,
  "ranked_donors": [
    {
      "user_id": "a1b2c3d4-0000-0000-0000-000000000001",
      "rank": 1,
      "suitability_score": 98,
      "reason": "Exact O- match and closest proximity to trauma center."
    }
  ],
  "notifications": [
    {
      "recipient_type": "Donor",
      "recipient_id": "a1b2c3d4-0000-0000-0000-000000000001",
      "title": "CRITICAL: Urgent O- Blood Needed at City General Trauma Center",
      "message": "A critical patient urgently requires O- blood for trauma surgery.",
      "email_subject": "Urgent LifeLink Alert: O- Blood Needed Immediately",
      "email_body": "Dear John,\n\nCity General Trauma Center has logged a critical request for O- blood...",
      "sms_body": "LifeLink Critical: O- blood needed immediately at City General Trauma Center. Open app to respond."
    },
    {
      "recipient_type": "Hospital",
      "recipient_id": "e4b60000-0000-0000-0000-000000000002",
      "title": "[CRITICAL] Urgent Blood Shortage Notice (O-)",
      "message": "City General Trauma Center requires 2 units of O- blood. Please check stock reserves."
    }
  ]
}
```

### 3. Standalone Donor Ranking
`POST /rank-donors`

### 4. Standalone Notification Generation
`POST /generate-notifications`

---

## 🧪 Testing

To test the agent using `pytest`:
```bash
pytest
```
Or execute manual API requests via the Swagger UI at `http://localhost:8000/docs`.
