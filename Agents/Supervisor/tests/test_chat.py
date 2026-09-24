from unittest.mock import AsyncMock, patch

from services import guardrails
from tests.conftest import KEY, ok

DONOR_SNAPSHOT = {
    "role": "Donor",
    "profile": {"bloodGroup": "O+", "bloodGroupConfirmed": False, "canDonateNow": True},
    "acceptances": [{"acceptanceId": "11111111-2222-3333-4444-555555555555", "status": "ScreeningPending",
                     "requestBloodGroup": "O+", "hospitalName": "General Hospital"}],
    "requests": [],
    "notifications": {"unread": 1},
}


def chat(client, message, **extra):
    body = {"message": message, "user": {"role": extra.pop("role", "Donor")}, "snapshot": extra.pop("snapshot", {}), **extra}
    res = client.post("/chat", headers=KEY, json=body)
    assert res.status_code == 200, res.text
    return res.json()


def test_chat_requires_internal_key(client):
    assert client.post("/chat", json={"message": "hi"}).status_code == 401


def test_medical_question_is_answered_from_medical_guidance_with_sources(client):
    body = chat(client, "Can I donate blood after getting a tattoo?")
    medical = [s for s in body["segments"] if s["type"] == "medical"]
    assert medical, body
    assert medical[0]["sources"] and all(src["url"] and src["publisher"] for src in medical[0]["sources"])
    assert guardrails.MEDICAL_DISCLAIMER in medical[0]["text"]


def test_platform_question_is_answered_from_the_lifelink_guide(client):
    body = chat(client, "How do I offer blood to another hospital through LifeLink?")
    platform = [s for s in body["segments"] if s["type"] == "platform"]
    assert platform and "transfer" in platform[0]["text"].lower()
    assert all(s["type"] != "medical" for s in body["segments"])


def test_account_question_uses_only_the_snapshot(client):
    body = chat(client, "What is my status?", snapshot=DONOR_SNAPSHOT)
    account = [s for s in body["segments"] if s["type"] == "account"][0]
    assert "O+" in account["text"] and "continue your screening interview" in account["text"]
    assert any(a["route"].endswith("/screening") for a in body["actions"])


def test_screening_question_is_explained_then_the_interview_question_repeats(client):
    turn = ok({"kind": "question", "query": "What is G6PD deficiency?",
               "reply": "Let's continue: Have you ever had G6PD deficiency?",
               "screening": {"status": "InProgress", "section": "Medical History", "sectionIndex": 5, "answered": 20, "total": 60}})
    with patch("services.agent_clients.agent_clients.screening_turn", new=AsyncMock(return_value=turn)) as call:
        body = chat(client, "What is G6PD?", mode="screening", acceptanceId="a-1")
    call.assert_awaited_once_with("a-1", "What is G6PD?")
    types = [s["type"] for s in body["segments"]]
    assert types == ["medical", "screening"]
    assert "G6PD" in body["segments"][0]["text"]
    assert body["screening"]["sectionIndex"] == 5


def test_screening_answer_is_passed_through(client):
    turn = ok({"kind": "answer", "reply": "Thanks. Did you sleep at least 6 hours last night?", "screening": {"status": "InProgress"}})
    with patch("services.agent_clients.agent_clients.screening_turn", new=AsyncMock(return_value=turn)):
        body = chat(client, "Yes", mode="screening", acceptanceId="a-1")
    assert body["segments"] == [{"type": "screening", "text": "Thanks. Did you sleep at least 6 hours last night?", "sources": []}]


def test_screening_without_message_resumes_the_session(client):
    with patch("services.agent_clients.agent_clients.screening_session", new=AsyncMock(return_value=ok({"kind": "resume", "reply": "Welcome back.", "screening": {"status": "InProgress"}}))) as session:
        chat(client, "", mode="screening", acceptanceId="a-1")
    session.assert_awaited_once_with("a-1")


def test_screening_requires_an_acceptance(client):
    assert client.post("/chat", headers=KEY, json={"mode": "screening", "message": "yes"}).status_code == 400


def test_guardrails_hide_details_not_in_the_snapshot():
    snapshot = {"profile": {"email": "me@example.com"}}
    text = "Contact me@example.com or other@example.com, call 0771234567, request 11111111-2222-3333-4444-555555555555, due 2026-09-30."
    redacted = guardrails.redact(text, snapshot)
    assert "me@example.com" in redacted
    assert "other@example.com" not in redacted and "0771234567" not in redacted
    assert "11111111-2222-3333-4444-555555555555" not in redacted
    assert "2026-09-30" in redacted  # dates are not treated as phone numbers


def test_guardrails_remove_ai_approval_claims():
    assert "I have approved" not in guardrails.enforce_authority("Good news, I have approved your donation.")
    assert "the doctor" in guardrails.enforce_authority("You are approved to donate tomorrow.")
