import pytest
import asyncio
from graph.workflow import screening_agent_graph
from graph.state import ScreeningAgentState
from database.db import init_db


@pytest.mark.asyncio
async def test_full_langgraph_workflow_execution():
    """
    Tests full LangGraph 11-node workflow:
    START -> Load Acceptance -> Create Session -> Ask Questions -> Collect Answers -> Validate Answers
    -> Gemini Analysis -> Risk Classification -> Generate Report -> Store Report -> Mark Completed
    -> Send To Doctor Review Queue -> END
    """
    acceptance_id = "test-acc-langgraph-001"
    initial_state: ScreeningAgentState = {
        "acceptance_id": acceptance_id,
        "session_id": None,
        "donor_user_id": "test-donor-101",
        "blood_request_id": "test-req-202",
        "donor_profile": {
            "fullName": "Jane Donor",
            "gender": "Female",
            "bloodGroup": "O+",
            "email": "jane@example.com"
        },
        "blood_request": {
            "bloodGroup": "O+",
            "unitsRequired": 2,
            "priority": "Urgent"
        },
        "answers": [
            {"question_id": "GH_1", "question_text": "Feeling well?", "answer": "Yes"},
            {"question_id": "GH_2", "question_text": "Fever?", "answer": "No"},
            {"question_id": "MED_1", "question_text": "Medications?", "answer": "No"},
            {"question_id": "INF_HISTORY", "question_text": "Infectious history?", "answer": "No"},
            {"question_id": "CONS_TRUTH", "question_text": "Truthful?", "answer": "Yes"},
            {"question_id": "CONS_TEST", "question_text": "Consent?", "answer": "Yes"}
        ],
        "current_question_index": 6,
        "validation_passed": False,
        "analysis_result": {},
        "risk_level": "",
        "recommendation": "",
        "report_id": None,
        "report_payload": {},
        "status": "Started"
    }

    final_state = await screening_agent_graph.ainvoke(initial_state)

    assert final_state is not None
    assert final_state["status"] == "InDoctorReviewQueue"
    assert final_state["validation_passed"] is True
    assert final_state["risk_level"] in ["LOW", "MEDIUM", "HIGH"]
    assert final_state["recommendation"] in ["Eligible", "Temporarily Deferred", "Requires Doctor Review"]
    assert final_state["report_id"] is not None
    assert "summary" in final_state["analysis_result"]
