import os
import json
import pytest
from database.db import SessionLocal, init_db
from services.screening_service import screening_service
from services.report_service import report_service
from config.settings import settings


@pytest.mark.asyncio
async def test_full_report_generation_and_json_export():
    db = SessionLocal()
    acceptance_id = "test-acc-report-003"
    try:
        # Start and answer questions
        await screening_service.start_screening_session(db, acceptance_id)
        await screening_service.submit_answer(db, acceptance_id, "GH_1", "Yes, feeling energetic")
        await screening_service.submit_answer(db, acceptance_id, "MED_1", "No")
        await screening_service.submit_answer(db, acceptance_id, "CONS_TRUTH", "Yes, completely truthful")
        
        # Complete screening to generate report
        report_entity = await screening_service.complete_screening(db, acceptance_id)
        assert report_entity is not None

        # Retrieve full report via report_service
        full_report = await report_service.get_full_report_by_acceptance_id(db, acceptance_id)
        assert full_report is not None

        # Verify Sections A through J
        # Section A: Donor Info
        assert full_report.section_a_donor_info.full_name is not None
        assert full_report.section_a_donor_info.email is not None

        # Section B: Blood Request Info
        assert full_report.section_b_blood_request_info.blood_group is not None
        assert full_report.section_b_blood_request_info.units_required > 0

        # Section C: Complete Questionnaire (100% visible)
        assert len(full_report.section_c_questionnaire) >= 3
        q_ids = [q.question_id for q in full_report.section_c_questionnaire]
        assert "GH_1" in q_ids
        assert "MED_1" in q_ids
        assert "CONS_TRUTH" in q_ids

        # Section D: AI Analysis
        assert full_report.section_d_ai_analysis.summary is not None

        # Section E: Risk Level
        assert full_report.section_e_risk_level in ["LOW", "MEDIUM", "HIGH"]

        # Section F: Recommendation
        assert full_report.section_f_recommendation in ["Eligible", "Temporarily Deferred", "Requires Doctor Review"]

        # Section J: Audit Info
        assert full_report.section_j_audit_info.acceptance_id == acceptance_id
        assert full_report.section_j_audit_info.started_at is not None
        assert full_report.section_j_audit_info.report_created_at is not None

        # Verify JSON export file exists on disk
        json_path = settings.REPORTS_DIR / f"report_{acceptance_id}.json"
        assert json_path.exists()

        with open(json_path, "r", encoding="utf-8") as f:
            disk_data = json.load(f)
            assert disk_data["report_id"] == report_entity.report_id
            assert "section_c_questionnaire" in disk_data
    finally:
        db.close()
