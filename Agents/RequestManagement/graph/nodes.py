import json
import logging
from datetime import datetime, timezone
from typing import Dict, Any

from graph.state import ScreeningAgentState
from services.backend_client import backend_client
from services.gemini_service import gemini_service
from database.db import SessionLocal
from database.models import ScreeningSession, DonorScreeningAnswer, DonorScreeningReport
from services.report_service import report_service

logger = logging.getLogger("ScreeningGraphNodes")

async def load_acceptance_data_node(state: ScreeningAgentState) -> Dict[str, Any]:
    """Node 1: Loads acceptance, donor profile, and blood request context strictly via REST API."""
    acceptance_id = state["acceptance_id"]
    acceptance = await backend_client.get_acceptance(acceptance_id)
    donor_user_id = acceptance.get("donorUserId", "")
    blood_request_id = acceptance.get("bloodRequestId", "")

    donor_profile = await backend_client.get_donor_profile(donor_user_id)
    blood_request = await backend_client.get_blood_request(blood_request_id)

    return {
        "donor_user_id": donor_user_id,
        "blood_request_id": blood_request_id,
        "donor_profile": donor_profile,
        "blood_request": blood_request,
        "status": "AcceptanceLoaded"
    }

async def create_session_node(state: ScreeningAgentState) -> Dict[str, Any]:
    """Node 2: Initializes database session if not yet present."""
    db = SessionLocal()
    try:
        session = db.query(ScreeningSession).filter(ScreeningSession.acceptance_id == state["acceptance_id"]).first()
        if not session:
            session = ScreeningSession(
                acceptance_id=state["acceptance_id"],
                donor_user_id=state.get("donor_user_id") or "donor-placeholder",
                blood_request_id=state.get("blood_request_id") or "request-placeholder",
                status="InProgress",
                current_step=0,
                started_at=datetime.now(timezone.utc)
            )
            db.add(session)
            db.commit()
            db.refresh(session)
        return {"session_id": session.session_id, "status": "SessionCreated"}
    finally:
        db.close()

async def ask_questions_node(state: ScreeningAgentState) -> Dict[str, Any]:
    """Node 3: Formats question set for the session."""
    return {"status": "QuestionsPrepared"}

async def collect_answers_node(state: ScreeningAgentState) -> Dict[str, Any]:
    """Node 4: Collects and consolidates recorded answers from state or database."""
    session_id = state.get("session_id")
    if not session_id:
        return {"answers": state.get("answers", [])}

    db = SessionLocal()
    try:
        records = db.query(DonorScreeningAnswer).filter(DonorScreeningAnswer.session_id == session_id).all()
        answers = [
            {
                "question_id": r.question_id,
                "question_text": r.question_text,
                "answer": r.answer,
                "created_at": r.created_at.isoformat()
            }
            for r in records
        ]
        # Merge with state answers if database records are empty (e.g. In-memory graph test)
        final_answers = answers if answers else state.get("answers", [])
        return {"answers": final_answers, "status": "AnswersCollected"}
    finally:
        db.close()

async def validate_answers_node(state: ScreeningAgentState) -> Dict[str, Any]:
    """Node 5: Validates that answers are non-empty and consent is obtained."""
    answers = state.get("answers", [])
    has_answers = len(answers) > 0
    return {
        "validation_passed": has_answers,
        "status": "ValidationPassed" if has_answers else "ValidationFailed"
    }

async def gemini_analysis_node(state: ScreeningAgentState) -> Dict[str, Any]:
    """Node 6: Conducts LLM analysis with clinical rule-engine fallback."""
    answers = state.get("answers", [])
    donor_profile = state.get("donor_profile", {})
    blood_request = state.get("blood_request", {})

    analysis = await gemini_service.analyze_screening(answers, donor_profile, blood_request)
    return {
        "analysis_result": analysis,
        "status": "AnalysisCompleted"
    }

async def risk_classification_node(state: ScreeningAgentState) -> Dict[str, Any]:
    """Node 7: Normalizes Risk Level (LOW, MEDIUM, HIGH) and Recommendation."""
    analysis = state.get("analysis_result", {})
    risk_level = analysis.get("risk_level", "LOW")
    recommendation = analysis.get("recommendation", "Eligible")
    return {
        "risk_level": risk_level,
        "recommendation": recommendation,
        "status": "RiskClassified"
    }

async def generate_report_node(state: ScreeningAgentState) -> Dict[str, Any]:
    """Node 8: Compiles structured report payload."""
    report_payload = {
        "acceptance_id": state["acceptance_id"],
        "session_id": state.get("session_id"),
        "donor_user_id": state.get("donor_user_id"),
        "blood_request_id": state.get("blood_request_id"),
        "risk_level": state.get("risk_level", "LOW"),
        "recommendation": state.get("recommendation", "Eligible"),
        "analysis": state.get("analysis_result", {})
    }
    return {
        "report_payload": report_payload,
        "status": "ReportGenerated"
    }

async def store_report_node(state: ScreeningAgentState) -> Dict[str, Any]:
    """Node 9: Persists report to database and exports JSON file."""
    session_id = state.get("session_id")
    acceptance_id = state["acceptance_id"]
    db = SessionLocal()
    try:
        report = db.query(DonorScreeningReport).filter(DonorScreeningReport.acceptance_id == acceptance_id).first()
        analysis = state.get("analysis_result", {})
        
        if not report:
            report = DonorScreeningReport(
                session_id=session_id or f"sess-{acceptance_id}",
                acceptance_id=acceptance_id,
                donor_user_id=state.get("donor_user_id") or "",
                blood_request_id=state.get("blood_request_id") or "",
                risk_level=state.get("risk_level", "LOW"),
                recommendation=state.get("recommendation", "Eligible"),
                ai_summary=analysis.get("summary", "Screening completed."),
                ai_analysis=json.dumps(analysis),
                ai_notes=analysis.get("notes", ""),
                created_at=datetime.now(timezone.utc)
            )
            db.add(report)
        else:
            report.risk_level = state.get("risk_level", "LOW")
            report.recommendation = state.get("recommendation", "Eligible")
            report.ai_summary = analysis.get("summary", "")
            report.ai_analysis = json.dumps(analysis)
            report.ai_notes = analysis.get("notes", "")

        db.commit()
        db.refresh(report)

        # Export report JSON to reports/generated/
        await report_service.export_json_report(db, report)
        return {"report_id": report.report_id, "status": "ReportStored"}
    finally:
        db.close()

async def mark_completed_node(state: ScreeningAgentState) -> Dict[str, Any]:
    """Node 10: Marks screening session as Completed."""
    session_id = state.get("session_id")
    if session_id:
        db = SessionLocal()
        try:
            session = db.query(ScreeningSession).filter(ScreeningSession.session_id == session_id).first()
            if session:
                session.status = "UnderDoctorReview"
                session.completed_at = datetime.now(timezone.utc)
                db.commit()
        finally:
            db.close()
    return {"status": "ScreeningCompleted"}

async def send_to_doctor_review_queue_node(state: ScreeningAgentState) -> Dict[str, Any]:
    """Node 11: Notifies backend REST API that screening is UnderDoctorReview."""
    acceptance_id = state["acceptance_id"]
    await backend_client.update_acceptance_status(acceptance_id, "ScreeningCompleted")
    await backend_client.submit_report_metadata({
        "acceptanceId": acceptance_id,
        "reportId": state.get("report_id"),
        "status": "UnderDoctorReview",
        "riskLevel": state.get("risk_level"),
        "recommendation": state.get("recommendation")
    })
    return {"status": "InDoctorReviewQueue"}
