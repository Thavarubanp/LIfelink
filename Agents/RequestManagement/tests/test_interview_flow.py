import json
from unittest.mock import AsyncMock, patch

import pytest
from fastapi.testclient import TestClient

from app import app
from services.backend_client import BackendUnavailableError

KEY = {"X-Internal-Key": "test-key"}
PROFILE = {"userId": "u-1", "fullName": "Kamal Perera", "gender": "Male", "dateOfBirth": "1995-04-10T00:00:00",
           "email": "kamal@example.com", "phoneNumber": "0771234567", "address": "Colombo", "bloodGroup": "O+"}

# A simple valid answer for every question type
ANSWERS = {"confirm": "yes", "yes_no": "no", "text": "None", "date": "yes", "number": "1", "select": "yes", "checklist": "none"}
OVERRIDES = {"P_NIC": "199512345678", "P_OCCUPATION": "Teacher", "P_EMERGENCY_NAME": "Nimal", "P_EMERGENCY_PHONE": "0712345678",
             "CH_WELL": "yes", "CH_ATE_4H": "yes", "CH_SLEPT_6H": "yes", "BE_AGE_18_60": "yes", "BE_WEIGHT_50": "yes", "CONSENT": "yes"}


class FakeBackend:
    def __init__(self, status="Accepted"):
        self.status = status
        self.reports = []

    async def get_acceptance(self, acceptance_id):
        return {"acceptanceId": acceptance_id, "bloodRequestId": "r-1", "donorUserId": "u-1", "status": self.status}

    async def get_donor_profile(self, donor_user_id):
        return dict(PROFILE)

    async def mark_screening_started(self, acceptance_id):
        self.status = "ScreeningPending"
        return True

    async def submit_report(self, report):
        self.reports.append(report)
        self.status = "ScreeningCompleted"
        return {"success": True}


@pytest.fixture
def backend():
    fake = FakeBackend()
    with patch("services.screening_service.backend_client", fake):
        yield fake


@pytest.fixture
def client():
    with TestClient(app) as c:
        yield c


def turn(client, message):
    res = client.post("/api/agent/screening/turn/a-1", headers=KEY, json={"message": message})
    assert res.status_code == 200, res.text
    return res.json()


def answer_everything(client):
    body = client.get("/api/agent/screening/session/a-1", headers=KEY).json()
    for _ in range(80):
        question = body["screening"]["question"]
        if question is None:
            break
        body = turn(client, OVERRIDES.get(question["question_id"], ANSWERS[question["type"]]))
    return body


def test_endpoints_require_the_internal_key(client):
    assert client.get("/api/agent/screening/session/a-1").status_code == 401
    assert client.post("/api/agent/screening/turn/a-1", json={"message": "yes"}).status_code == 401


def test_interview_starts_with_prefilled_section_one(client, backend):
    body = client.get("/api/agent/screening/session/a-1", headers=KEY).json()
    assert body["kind"] == "resume"
    assert "Section 1 of 12" in body["reply"] and "Kamal Perera" in body["reply"]
    assert backend.status == "ScreeningPending"  # the backend is told the interview started


def test_donor_questions_are_explained_and_the_question_repeats(client, backend):
    client.get("/api/agent/screening/session/a-1", headers=KEY)
    turn(client, "yes")  # name confirmed
    body = turn(client, "What is an NIC number?")
    assert body["kind"] == "question" and "NIC" in body["query"]
    assert "passport number" in body["reply"]
    assert body["screening"]["answered"] == 1  # nothing recorded for a question


def test_full_interview_submits_a_structured_report(client, backend):
    body = answer_everything(client)
    assert body["kind"] == "complete" and body["screening"]["isComplete"] is True
    assert len(backend.reports) == 1

    submitted = backend.reports[0]
    report = json.loads(submitted["reportJson"])
    assert submitted["recommendation"] == "Eligible" and report["risk_level"] == "LOW"
    assert [s["index"] for s in report["sections"]] == list(range(1, 13))
    assert report["donor"]["nic"] == "199512345678" and report["donor"]["age"] >= 18
    assert report["sections"][9]["confidential"] is True
    assert "doctor makes the final decision" in report["governance"]

    after = turn(client, "hello?")
    assert after["kind"] == "complete"


def test_updating_answers_creates_a_new_report_version(client, backend):
    answer_everything(client)
    backend.status = "ScreeningPending"  # donor chose "Update my answers" in LifeLink

    body = client.get("/api/agent/screening/session/a-1", headers=KEY).json()
    assert "previous answer" in body["reply"].lower()
    for _ in range(80):
        if body["screening"]["question"] is None:
            break
        body = turn(client, "same")
    assert len(backend.reports) == 2
    assert json.loads(backend.reports[1]["reportJson"])["report_version"] == 2


def test_report_that_failed_to_submit_is_retried_not_reported_as_sent(client, backend):
    backend.submit_report = AsyncMock(side_effect=BackendUnavailableError("down"))
    body = client.get("/api/agent/screening/session/a-1", headers=KEY).json()
    for _ in range(80):
        question = body["screening"]["question"]
        res = client.post("/api/agent/screening/turn/a-1", headers=KEY,
                          json={"message": OVERRIDES.get(question["question_id"], ANSWERS[question["type"]])})
        if res.status_code == 503:
            break
        body = res.json()
    assert res.status_code == 503  # the last answer is saved, the report did not reach the backend

    resumed = client.get("/api/agent/screening/session/a-1", headers=KEY).json()
    assert "not reached the doctor" in resumed["reply"] and resumed["screening"]["isComplete"] is False

    backend.submit_report = AsyncMock(return_value={"success": True})
    after = turn(client, "hello?")
    assert after["kind"] == "complete" and after["screening"]["isComplete"] is True
    assert backend.submit_report.await_count == 1


def test_closed_donation_stops_the_interview(client, backend):
    backend.status = "Cancelled"
    body = turn(client, "yes")
    assert body["kind"] == "closed"


def test_backend_unavailable_never_uses_made_up_data(client):
    failing = FakeBackend()
    failing.get_acceptance = AsyncMock(side_effect=BackendUnavailableError("down"))
    with patch("services.screening_service.backend_client", failing):
        res = client.post("/api/agent/screening/turn/a-1", headers=KEY, json={"message": "yes"})
    assert res.status_code == 503
