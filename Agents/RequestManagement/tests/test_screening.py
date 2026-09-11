import pytest
from database.db import SessionLocal, init_db
from services.screening_service import screening_service
from models.donor_screening import get_applicable_questions


@pytest.mark.asyncio
async def test_session_creation_and_progression():
    db = SessionLocal()
    acceptance_id = "test-acc-screening-001"
    try:
        session, first_q, total_est = await screening_service.start_screening_session(db, acceptance_id)
        assert session is not None
        assert session.acceptance_id == acceptance_id
        assert session.status == "InProgress"
        assert first_q.question_id == "GH_1"
        assert total_est > 10

        # Submit answer to GH_1
        sess_after, is_complete, next_q = await screening_service.submit_answer(
            db, acceptance_id, "GH_1", "Yes"
        )
        assert sess_after.current_step == 1
        assert is_complete is False
        assert next_q is not None
        assert next_q.question_id == "GH_2"
    finally:
        db.close()

def test_conditional_questioning_medications():
    """
    Validates dynamic questioning:
    If MED_1 is 'No', child medication questions are skipped.
    If MED_1 is 'Yes', follow-up questions are included.
    """
    # Case 1: Answered "No"
    answers_no = {"MED_1": "no"}
    q_no = get_applicable_questions(donor_gender="Male", current_answers=answers_no)
    q_ids_no = [q.question_id for q in q_no]

    assert "MED_1" in q_ids_no
    assert "MED_NAMES" not in q_ids_no
    assert "MED_ANTIBIOTICS" not in q_ids_no
    assert "MED_THINNERS" not in q_ids_no

    # Case 2: Answered "Yes"
    answers_yes = {"MED_1": "yes"}
    q_yes = get_applicable_questions(donor_gender="Male", current_answers=answers_yes)
    q_ids_yes = [q.question_id for q in q_yes]

    assert "MED_1" in q_ids_yes
    assert "MED_NAMES" in q_ids_yes
    assert "MED_ANTIBIOTICS" in q_ids_yes
    assert "MED_THINNERS" in q_ids_yes

def test_female_specific_question_pruning():
    """
    Validates that Section 10 Pregnancy questions are asked ONLY for female donors
    and omitted for male donors.
    """
    q_male = get_applicable_questions(donor_gender="Male")
    male_ids = [q.question_id for q in q_male]
    assert "PREG_STATUS" not in male_ids

    q_female = get_applicable_questions(donor_gender="Female")
    female_ids = [q.question_id for q in q_female]
    assert "PREG_STATUS" in female_ids

@pytest.mark.asyncio
async def test_screening_completion_and_risk_evaluation():
    db = SessionLocal()
    acceptance_id = "test-acc-screening-complete-002"
    try:
        session, _, _ = await screening_service.start_screening_session(db, acceptance_id)
        
        # Provide answers indicating high risk: infectious disease
        await screening_service.submit_answer(db, acceptance_id, "GH_1", "Yes")
        await screening_service.submit_answer(db, acceptance_id, "INF_HISTORY", "Yes")
        await screening_service.submit_answer(db, acceptance_id, "CONS_TRUTH", "Yes")

        report = await screening_service.complete_screening(db, acceptance_id)

        assert report is not None
        assert report.risk_level == "HIGH"
        assert report.recommendation in ["Requires Doctor Review", "Temporarily Deferred"]
        assert "Infectious disease history reported" in report.ai_analysis
        
        # Check session status updated
        db.refresh(session)
        assert session.status == "UnderDoctorReview"
    finally:
        db.close()
