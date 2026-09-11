import pytest
from database.db import SessionLocal, init_db
from services.screening_service import screening_service
from services.doctor_review_service import doctor_review_service
from models.doctor_review import DoctorReviewSubmission


@pytest.mark.asyncio
async def test_doctor_physical_review_and_decision():
    db = SessionLocal()
    acceptance_id = "test-acc-review-004"
    doctor_id = "doc-user-999"
    try:
        # 1. Setup completed screening report
        session, _, _ = await screening_service.start_screening_session(db, acceptance_id)
        await screening_service.submit_answer(db, acceptance_id, "GH_1", "Yes")
        report = await screening_service.complete_screening(db, acceptance_id)

        # 2. Doctor records vitals and Approves
        review_data = DoctorReviewSubmission(
            doctor_user_id=doctor_id,
            weight=68.5,
            blood_pressure="120/80",
            pulse_rate=72,
            temperature=36.8,
            hemoglobin=14.2,
            decision="Approved",
            doctor_notes="Donor in optimal physical health. All vital signs normal. Eligible for donation."
        )

        review_resp = await doctor_review_service.submit_doctor_review(db, report.report_id, review_data)

        assert review_resp is not None
        assert review_resp.report_id == report.report_id
        assert review_resp.decision == "Approved"
        assert review_resp.weight == 68.5
        assert review_resp.hemoglobin == 14.2

        # Verify session status transition to DoctorApproved
        db.refresh(session)
        assert session.status == "DoctorApproved"

        # 3. Retrieve doctor review
        retrieved = await doctor_review_service.get_doctor_review(db, report.report_id)
        assert retrieved is not None
        assert retrieved.doctor_user_id == doctor_id
        assert retrieved.decision == "Approved"
    finally:
        db.close()

@pytest.mark.asyncio
async def test_doctor_rejection_workflow():
    db = SessionLocal()
    acceptance_id = "test-acc-reject-005"
    doctor_id = "doc-user-888"
    try:
        session, _, _ = await screening_service.start_screening_session(db, acceptance_id)
        await screening_service.submit_answer(db, acceptance_id, "GH_1", "Yes")
        report = await screening_service.complete_screening(db, acceptance_id)

        review_data = DoctorReviewSubmission(
            doctor_user_id=doctor_id,
            weight=42.0,  # Below 45kg minimum limit
            blood_pressure="145/95",
            pulse_rate=98,
            temperature=37.1,
            hemoglobin=10.5,  # Anemic
            decision="Rejected",
            doctor_notes="Donor fails minimum weight criteria (42kg < 45kg) and low hemoglobin (10.5 g/dL)."
        )

        review_resp = await doctor_review_service.submit_doctor_review(db, report.report_id, review_data)
        assert review_resp.decision == "Rejected"

        db.refresh(session)
        assert session.status == "DoctorRejected"
    finally:
        db.close()
