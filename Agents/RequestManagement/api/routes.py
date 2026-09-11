import logging
from typing import Optional
from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy.orm import Session

from config.settings import settings
from api.dependencies import get_database_session
from models.api_models import (
    HealthCheckResponse,
    StartScreeningResponse,
    SessionProgressResponse,
    NextQuestionsResponse,
    SubmitAnswerRequest,
    SubmitAnswerResponse,
    CompleteScreeningResponse
)
from models.report import FullDoctorScreeningReport
from models.doctor_review import DoctorReviewSubmission, DoctorReviewResponse
from services.screening_service import screening_service
from services.report_service import report_service
from services.doctor_review_service import doctor_review_service

logger = logging.getLogger("ScreeningAgentRoutes")
router = APIRouter(prefix="/api/agent", tags=["Student 1 AI Donor Screening"])

# -------------------------------------------------------------
# HEALTH CHECK
# -------------------------------------------------------------
@router.get("/health", response_model=HealthCheckResponse)
def health_check():
    """Returns agent service operational status and configuration."""
    return HealthCheckResponse(
        status="Healthy",
        agent="LifeLink Student 1 - Donor Screening Agent",
        framework="FastAPI + LangGraph + Gemini",
        model=settings.MODEL_NAME,
        gemini_configured=bool(settings.GEMINI_API_KEY),
        database_url=settings.DATABASE_URL.split("@")[-1] if "@" in settings.DATABASE_URL else settings.DATABASE_URL
    )

# -------------------------------------------------------------
# SCREENING WORKFLOW ENDPOINTS
# -------------------------------------------------------------
@router.post("/screening/start/{acceptanceId}", response_model=StartScreeningResponse)
async def start_screening(
    acceptanceId: str,
    db: Session = Depends(get_database_session)
):
    """
    Initializes a new conversational screening session for the donor acceptance.
    Fetches donor/request context and returns session ID and Section 1 Question 1.
    """
    try:
        session, first_q, total_est = await screening_service.start_screening_session(db, acceptanceId)
        return StartScreeningResponse(
            session_id=session.session_id,
            acceptance_id=session.acceptance_id,
            donor_user_id=session.donor_user_id,
            blood_request_id=session.blood_request_id,
            status=session.status,
            total_estimated_questions=total_est,
            first_question=first_q
        )
    except Exception as ex:
        logger.error(f"Error starting screening for acceptance {acceptanceId}: {ex}", exc_info=True)
        raise HTTPException(status_code=status.HTTP_500_INTERNAL_SERVER_ERROR, detail=str(ex))

@router.get("/screening/session/{acceptanceId}", response_model=SessionProgressResponse)
async def get_session_progress(
    acceptanceId: str,
    db: Session = Depends(get_database_session)
):
    """Returns current progress and state of the donor's screening session."""
    session = await screening_service.get_session(db, acceptanceId)
    if not session:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=f"Screening session for acceptance {acceptanceId} not found.")

    return SessionProgressResponse(
        session_id=session.session_id,
        acceptance_id=session.acceptance_id,
        status=session.status,
        current_step=session.current_step,
        total_answered=len(session.answers),
        started_at=session.started_at,
        completed_at=session.completed_at
    )

@router.get("/screening/questions/{acceptanceId}", response_model=NextQuestionsResponse)
async def get_next_question(
    acceptanceId: str,
    db: Session = Depends(get_database_session)
):
    """
    Dynamically computes the next question in sequence based on conditional
    branch answers and donor demographics.
    """
    try:
        session = await screening_service.get_session(db, acceptanceId)
        if not session:
            raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=f"Session for acceptance {acceptanceId} not found.")

        next_q, is_complete, remaining = await screening_service.get_next_question(db, acceptanceId)
        return NextQuestionsResponse(
            session_id=session.session_id,
            status=session.status,
            next_question=next_q,
            is_complete=is_complete,
            remaining_count=remaining
        )
    except ValueError as ve:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(ve))
    except Exception as ex:
        logger.error(f"Error retrieving questions for acceptance {acceptanceId}: {ex}", exc_info=True)
        raise HTTPException(status_code=status.HTTP_500_INTERNAL_SERVER_ERROR, detail=str(ex))

@router.post("/screening/answer/{acceptanceId}", response_model=SubmitAnswerResponse)
async def submit_answer(
    acceptanceId: str,
    request: SubmitAnswerRequest,
    db: Session = Depends(get_database_session)
):
    """
    Submits a donor's answer to a health screening question and advances session.
    """
    try:
        session, is_complete, next_q = await screening_service.submit_answer(
            db, acceptanceId, request.question_id, request.answer
        )
        return SubmitAnswerResponse(
            session_id=session.session_id,
            question_id=request.question_id,
            status=session.status,
            is_complete=is_complete,
            next_question=next_q
        )
    except ValueError as ve:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(ve))
    except Exception as ex:
        logger.error(f"Error submitting answer for acceptance {acceptanceId}: {ex}", exc_info=True)
        raise HTTPException(status_code=status.HTTP_500_INTERNAL_SERVER_ERROR, detail=str(ex))

@router.post("/screening/complete/{acceptanceId}", response_model=CompleteScreeningResponse)
async def complete_screening(
    acceptanceId: str,
    db: Session = Depends(get_database_session)
):
    """
    Finalizes donor screening, executes Gemini analysis / clinical risk evaluation,
    generates full report, exports JSON report to disk, and enqueues for doctor review.
    """
    try:
        report = await screening_service.complete_screening(db, acceptanceId)
        json_path = settings.REPORTS_DIR / f"report_{acceptanceId}.json"
        
        import json
        analysis_data = json.loads(report.ai_analysis) if report.ai_analysis else {}
        flags = analysis_data.get("flags", [])

        return CompleteScreeningResponse(
            session_id=report.session_id,
            report_id=report.report_id,
            acceptance_id=report.acceptance_id,
            status="UnderDoctorReview",
            risk_level=report.risk_level,
            recommendation=report.recommendation,
            summary=report.ai_summary,
            flags=flags,
            report_json_path=str(json_path)
        )
    except ValueError as ve:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(ve))
    except Exception as ex:
        logger.error(f"Error completing screening for acceptance {acceptanceId}: {ex}", exc_info=True)
        raise HTTPException(status_code=status.HTTP_500_INTERNAL_SERVER_ERROR, detail=str(ex))

# -------------------------------------------------------------
# REPORT RETRIEVAL ENDPOINTS
# -------------------------------------------------------------
@router.get("/report/{acceptanceId}", response_model=FullDoctorScreeningReport)
async def get_report_by_acceptance(
    acceptanceId: str,
    db: Session = Depends(get_database_session)
):
    """
    Doctor views the full screening report (Sections A to J).
    Displays every question asked, every donor answer, AI summary, AI flags, and recommendations.
    """
    report = await report_service.get_full_report_by_acceptance_id(db, acceptanceId)
    if not report:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Screening report for acceptance {acceptanceId} was not found."
        )
    return report

@router.get("/report/session/{sessionId}", response_model=FullDoctorScreeningReport)
async def get_report_by_session(
    sessionId: str,
    db: Session = Depends(get_database_session)
):
    """Doctor retrieves full report by session ID."""
    report = await report_service.get_full_report_by_session_id(db, sessionId)
    if not report:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Screening report for session {sessionId} was not found."
        )
    return report

# -------------------------------------------------------------
# DOCTOR PHYSICAL REVIEW & FINAL DECISION ENDPOINTS
# -------------------------------------------------------------
@router.post("/review/{reportId}", response_model=DoctorReviewResponse)
async def submit_doctor_review(
    reportId: str,
    review_data: DoctorReviewSubmission,
    db: Session = Depends(get_database_session)
):
    """
    Doctor enters physical examination vitals (weight, BP, pulse, temp, hemoglobin)
    and records final decision: Approved, Rejected, or FurtherScreeningRequired.
    """
    try:
        return await doctor_review_service.submit_doctor_review(db, reportId, review_data)
    except ValueError as ve:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(ve))
    except Exception as ex:
        logger.error(f"Error submitting doctor review for report {reportId}: {ex}", exc_info=True)
        raise HTTPException(status_code=status.HTTP_500_INTERNAL_SERVER_ERROR, detail=str(ex))

@router.get("/review/{reportId}", response_model=DoctorReviewResponse)
async def get_doctor_review(
    reportId: str,
    db: Session = Depends(get_database_session)
):
    """Retrieves existing doctor review and vitals for a given report."""
    review = await doctor_review_service.get_doctor_review(db, reportId)
    if not review:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Doctor review for report {reportId} not found."
        )
    return review
