import json
import logging
from datetime import datetime
from pathlib import Path
from typing import Optional, Dict, Any
from sqlalchemy.orm import Session

from config.settings import settings
from database.models import DonorScreeningReport, ScreeningSession, DoctorReview
from models.report import (
    FullDoctorScreeningReport,
    DonorProfileInfo,
    BloodRequestInfo,
    QuestionAnswerItem,
    AiAnalysisDetail,
    PhysicalScreeningVitals,
    AuditInfo
)
from services.backend_client import backend_client

logger = logging.getLogger("ReportService")

class ReportService:
    """
    Manages complete doctor report generation, JSON file export, and retrieval.
    Guarantees that 100% of questions and answers are visible to the reviewing doctor.
    """

    async def get_full_report_by_acceptance_id(self, db: Session, acceptance_id: str) -> Optional[FullDoctorScreeningReport]:
        report_entity = db.query(DonorScreeningReport).filter(DonorScreeningReport.acceptance_id == acceptance_id).first()
        if not report_entity:
            return None
        return await self._build_full_report(db, report_entity)

    async def get_full_report_by_session_id(self, db: Session, session_id: str) -> Optional[FullDoctorScreeningReport]:
        report_entity = db.query(DonorScreeningReport).filter(DonorScreeningReport.session_id == session_id).first()
        if not report_entity:
            return None
        return await self._build_full_report(db, report_entity)

    async def export_json_report(self, db: Session, report_entity: DonorScreeningReport) -> Path:
        """
        Exports the full report to reports/generated/report_{acceptanceId}.json
        """
        full_report = await self._build_full_report(db, report_entity)
        file_path = settings.REPORTS_DIR / f"report_{report_entity.acceptance_id}.json"
        
        with open(file_path, "w", encoding="utf-8") as f:
            f.write(full_report.model_dump_json(indent=2))
            
        logger.info(f"Screening report exported successfully to {file_path}")
        return file_path

    async def _build_full_report(self, db: Session, report: DonorScreeningReport) -> FullDoctorScreeningReport:
        session: ScreeningSession = report.session
        
        # 1. Fetch live profiles from backend via REST API
        donor_dict = await backend_client.get_donor_profile(report.donor_user_id)
        blood_request_dict = await backend_client.get_blood_request(report.blood_request_id)
        
        donor_info = DonorProfileInfo(
            user_id=report.donor_user_id,
            full_name=donor_dict.get("fullName", "Donor"),
            email=donor_dict.get("email", ""),
            phone=donor_dict.get("phone"),
            nic=donor_dict.get("nic"),
            blood_group=donor_dict.get("bloodGroup"),
            gender=donor_dict.get("gender"),
            address=donor_dict.get("address")
        )

        blood_request_info = BloodRequestInfo(
            blood_request_id=report.blood_request_id,
            hospital_id=blood_request_dict.get("hospitalId", ""),
            hospital_name=blood_request_dict.get("hospitalName"),
            patient_user_id=blood_request_dict.get("patientUserId", ""),
            patient_name=blood_request_dict.get("patientName"),
            blood_group=blood_request_dict.get("bloodGroup", ""),
            units_required=blood_request_dict.get("unitsRequired", 1),
            priority=blood_request_dict.get("priority", "Normal"),
            reason=blood_request_dict.get("reason")
        )

        # 2. Extract questionnaire answers
        from models.donor_screening import QUESTION_BANK
        q_bank_map = {q.question_id: q.section for q in QUESTION_BANK}

        questionnaire_items = [
            QuestionAnswerItem(
                question_id=a.question_id,
                section=q_bank_map.get(a.question_id, "Screening Questionnaire"),
                question_text=a.question_text,
                answer=a.answer,
                answered_at=a.created_at
            )
            for a in session.answers
        ]

        # 3. Parse AI analysis JSON
        try:
            analysis_dict = json.loads(report.ai_analysis) if report.ai_analysis else {}
        except Exception:
            analysis_dict = {}

        ai_analysis = AiAnalysisDetail(
            summary=report.ai_summary,
            findings=analysis_dict.get("findings", []),
            risks=analysis_dict.get("risks", []),
            flags=analysis_dict.get("flags", []),
            notes=report.ai_notes
        )

        # 4. Doctor review if performed
        review: Optional[DoctorReview] = report.review
        physical_screening = None
        doctor_decision = None
        doctor_notes = None
        reviewed_at = None
        doctor_user_id = None

        if review:
            physical_screening = PhysicalScreeningVitals(
                weight=review.weight,
                blood_pressure=review.blood_pressure,
                pulse_rate=review.pulse_rate,
                temperature=review.temperature,
                hemoglobin=review.hemoglobin
            )
            doctor_decision = review.decision
            doctor_notes = review.doctor_notes
            reviewed_at = review.reviewed_at
            doctor_user_id = review.doctor_user_id

        # 5. Audit information
        audit_info = AuditInfo(
            acceptance_id=report.acceptance_id,
            blood_request_id=report.blood_request_id,
            donor_user_id=report.donor_user_id,
            doctor_user_id=doctor_user_id,
            session_id=report.session_id,
            started_at=session.started_at,
            completed_at=session.completed_at,
            report_created_at=report.created_at,
            reviewed_at=reviewed_at
        )

        return FullDoctorScreeningReport(
            report_id=report.report_id,
            section_a_donor_info=donor_info,
            section_b_blood_request_info=blood_request_info,
            section_c_questionnaire=questionnaire_items,
            section_d_ai_analysis=ai_analysis,
            section_e_risk_level=report.risk_level,
            section_f_recommendation=report.recommendation,
            section_g_physical_screening=physical_screening,
            section_h_doctor_decision=doctor_decision,
            section_i_doctor_notes=doctor_notes,
            section_j_audit_info=audit_info
        )

report_service = ReportService()
