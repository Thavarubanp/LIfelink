import json
from unittest.mock import AsyncMock, patch

import pytest
from fastapi.testclient import TestClient

from app import app

KEY = {"X-Internal-Key": "test-key"}
PROFILE = {"userId": "u-1", "fullName": "Kamal Perera", "gender": "Male", "dateOfBirth": "1995-04-10T00:00:00",
           "email": "kamal@example.test", "phoneNumber": "0771234567", "address": "Colombo", "bloodGroup": "O+"}

# Structured answers for every question (what the inputs inside the chat bubble send)
STRUCTURED = {
    "Q1": {"P_NAME": "Kamal Perera", "P_DOB": "1995-04-10", "P_GENDER": "Male"},
    "Q2": {"P_WEIGHT_KG": "68", "P_CONTACT": "0771234567"},
    "Q3": {"DH_BEFORE": "No"},
    "Q4": {"CH_WELL": "Yes", "CH_INFECTION_2W": "No", "CH_MEAL_4H": "Yes", "CH_SLEEP_6H": "Yes", "CH_ALCOHOL_24H": "No"},
    "Q5": {"MH_CONDITIONS": [], "MH_MEDICINES": "No"},
    "Q6": {"RE_EVENTS": [], "TR_ABROAD": "No"},
    "Q7": {"IR_RISK": "No"},
}


class FakeBackend:
    def __init__(self, status="Accepted", profile=None, suspended=False):
        self.status = status
        self.profile = profile or dict(PROFILE)
        self.suspended = suspended
        self.reports = []

    async def get_acceptance(self, acceptance_id):
        return {"acceptanceId": acceptance_id, "bloodRequestId": "r-1", "donorUserId": "u-1", "status": self.status,
                "requestSuspended": self.suspended}

    async def get_donor_profile(self, donor_user_id):
        return dict(self.profile)

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
    with patch("services.screening_service.backend_client", fake), patch("api.routes.backend_client", fake):
        yield fake


@pytest.fixture
def client():
    with TestClient(app) as c:
        yield c


def session(client):
    return client.get("/api/agent/screening/session/a-1", headers=KEY).json()


def turn(client, message="", structured=None):
    res = client.post("/api/agent/screening/turn/a-1", headers=KEY, json={"message": message, "structured": structured})
    assert res.status_code == 200, res.text
    return res.json()


def answer_all(client, overrides=None):
    body = session(client)
    for _ in range(20):
        question = body["screening"]["question"]
        if question is None or question["question_id"] == "CONFIRM_TRUE":
            break
        body = turn(client, structured={**STRUCTURED[question["question_id"]], **(overrides or {}).get(question["question_id"], {})})
    return body


def test_endpoints_require_the_internal_key(client):
    assert client.get("/api/agent/screening/session/a-1").status_code == 401
    assert client.post("/api/agent/screening/turn/a-1", json={"message": "yes"}).status_code == 401
    assert client.put("/api/agent/screening/answers/a-1", json={"answers": {}}).status_code == 401


def test_interview_starts_with_question_one_prefilled_from_the_profile(client, backend):
    body = session(client)
    assert body["kind"] == "resume" and "Question 1 of 7" in body["reply"]
    question = body["screening"]["question"]
    defaults = {p["id"]: p["default"] for p in question["parts"]}
    assert defaults["P_NAME"] == "Kamal Perera" and defaults["P_DOB"] == "1995-04-10" and defaults["P_GENDER"] == "Male"
    assert body["screening"]["sectionCount"] == 7
    assert backend.status == "ScreeningPending"  # the backend is told the interview started


def test_seven_questions_then_the_confirmation_tick_then_the_report(client, backend):
    body = answer_all(client)
    assert body["screening"]["answered"] == 7
    assert body["screening"]["question"]["question_id"] == "CONFIRM_TRUE"
    assert backend.reports == []                       # nothing is sent before the tick

    body = turn(client, "send it")                     # not a tick
    assert body["kind"] == "clarify" and backend.reports == []
    body = turn(client, structured={"CONFIRM_TRUE": "Yes"})
    assert body["kind"] == "complete" and "NIC" in body["reply"]
    report = json.loads(backend.reports[0]["reportJson"])
    assert report["schema"] == "lifelink.screening.v2" and len(report["sections"]) == 7
    assert report["sections"][6]["confidential"] is True
    assert report["risk_level"] == "LOW" and report["flags"] == []
    assert report["form_answers"]["P_WEIGHT_KG"] == "68" and "CONFIRM_TRUE" not in report["form_answers"]
    # The male donor was never shown the pregnancy part
    assert "FD_STATUS" not in report["form_answers"]


def test_free_text_confirms_prefill_and_missing_parts_get_one_short_follow_up(client, backend):
    session(client)
    body = turn(client, "yes, that's right")          # Q1 from the profile
    assert body["screening"]["question"]["question_id"] == "Q2"
    body = turn(client, "I weigh 68 kilos and my number is 0771234567")  # no LLM in tests: one part at a time
    assert body["kind"] == "clarify" and body["reply"].endswith("Weight (kg)?")
    body = turn(client, structured={"P_WEIGHT_KG": "68"})
    assert body["screening"]["question"]["follow_up"] is True        # same question, not a new one
    assert body["screening"]["question"]["missing"] == ["P_CONTACT"] and body["screening"]["answered"] == 1
    body = turn(client, "0771234567")
    assert body["screening"]["question"]["question_id"] == "Q3" and body["screening"]["answered"] == 2


def test_a_forgotten_last_donation_date(client, backend):
    session(client)
    turn(client, structured=STRUCTURED["Q1"])
    turn(client, structured=STRUCTURED["Q2"])
    body = turn(client, "yes")
    assert body["screening"]["question"]["missing"] == ["DH_LAST_DATE"]
    body = turn(client, "I don't remember")
    assert body["screening"]["question"]["question_id"] == "Q4"   # never asked again
    body = answer_all(client)
    turn(client, structured={"CONFIRM_TRUE": "Yes"})
    report = json.loads(backend.reports[0]["reportJson"])
    assert report["form_answers"]["DH_LAST_DATE"] == "Donor doesn't remember"
    assert any(f["code"] == "LAST_DONATION_UNKNOWN" and f["severity"] == "info" for f in report["flags"])


def test_a_question_is_explained_and_the_interview_continues(client, backend):
    session(client)
    turn(client, structured=STRUCTURED["Q1"])
    turn(client, structured=STRUCTURED["Q2"])
    turn(client, structured=STRUCTURED["Q3"])
    turn(client, structured=STRUCTURED["Q4"])
    body = turn(client, "What does hepatitis mean?")
    assert body["kind"] == "question" and "hepatitis" in body["query"]
    assert "Question 5 of 7" in body["reply"]                      # the question is repeated
    assert body["screening"]["question"]["question_id"] == "Q5"     # nothing recorded, not counted
    body = turn(client, structured=STRUCTURED["Q5"])
    assert body["screening"]["question"]["question_id"] == "Q6"


def test_the_llm_reads_free_text_but_never_question_seven(client, backend):
    extract = AsyncMock(return_value={"CH_WELL": "Yes", "CH_INFECTION_2W": "No", "CH_MEAL_4H": "Yes", "CH_SLEEP_6H": "Yes", "CH_ALCOHOL_24H": "No"})
    with patch("graph.workflow.gemini_service.extract_answers", extract):
        session(client)
        for q in ("Q1", "Q2", "Q3"):
            turn(client, structured=STRUCTURED[q])
        body = turn(client, "I'm fine today, had lunch, slept well and didn't drink")
        assert extract.await_count == 1 and body["screening"]["question"]["question_id"] == "Q5"
        turn(client, structured=STRUCTURED["Q5"])
        turn(client, structured=STRUCTURED["Q6"])
        body = turn(client, "well it depends what you mean by that")  # question 7: rules only
        assert extract.await_count == 1 and body["kind"] == "clarify"
        body = turn(client, "no")
        assert body["screening"]["question"]["question_id"] == "CONFIRM_TRUE"


def test_female_donors_get_the_pregnancy_part(client, backend):
    backend.profile = {**PROFILE, "gender": "Female", "fullName": "Nimali Silva"}
    body = answer_all(client, {"Q1": {"P_GENDER": "Female", "P_NAME": "Nimali Silva"}, "Q7": {"FD_STATUS": ["Breastfeeding"], "IR_RISK": "No"}})
    turn(client, structured={"CONFIRM_TRUE": "Yes"})
    report = json.loads(backend.reports[0]["reportJson"])
    assert any(f["code"] == "PREGNANCY" for f in report["flags"])


def test_screening_pauses_while_the_request_is_suspended(client, backend):
    backend.suspended = True
    body = session(client)
    assert body["kind"] == "paused" and body["screening"]["status"] == "Paused"


def test_the_edit_form_creates_a_new_version_without_the_chat(client, backend):
    answer_all(client)
    turn(client, structured={"CONFIRM_TRUE": "Yes"})
    backend.status = "ScreeningPending"   # the backend superseded version 1 when the donor saved the form

    edited = {k: v for q in STRUCTURED.values() for k, v in q.items()}
    edited.update({"RE_EVENTS": ["Tattoo"], "RE_TATTOO_DATE": "2026-01-15"})
    res = client.post("/api/agent/screening/answers/a-1/validate", headers=KEY, json={"answers": edited})
    assert res.json()["valid"] is False and any("confirm" in e.lower() for e in res.json()["errors"])
    missing = dict(edited, CONFIRM_TRUE="Yes")
    del missing["RE_TATTOO_DATE"]
    res = client.put("/api/agent/screening/answers/a-1", headers=KEY, json={"answers": missing})
    assert res.status_code == 400 and "Date of the tattoo" in res.text

    res = client.put("/api/agent/screening/answers/a-1", headers=KEY, json={"answers": dict(edited, CONFIRM_TRUE="Yes")})
    assert res.status_code == 202
    assert len(backend.reports) == 2                     # submitted in the background
    report = json.loads(backend.reports[1]["reportJson"])
    assert report["report_version"] == 2 and any(f["code"] == "TATTOO" for f in report["flags"])
