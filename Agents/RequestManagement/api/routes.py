import json
import logging

from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy.orm import Session

from api.dependencies import get_database_session
from api.security import require_internal_key
from config.settings import settings
from database.models import DonorScreeningReport
from graph.workflow import screening_turn_graph
from models.api_models import HealthCheckResponse, TurnRequest, TurnResponse
from models.donor_screening import render_question
from services.backend_client import BackendUnavailableError
from services.screening_service import ScreeningClosedError, screening_service

logger = logging.getLogger("ScreeningAgentRoutes")
router = APIRouter(prefix="/api/agent", tags=["Request Management Agent (Donor Screening)"])
protected = [Depends(require_internal_key)]


@router.get("/health", response_model=HealthCheckResponse)
def health_check():
    return HealthCheckResponse(status="Healthy", agent="LifeLink Request Management Agent", framework="FastAPI + LangGraph + Gemini",
                               model=settings.MODEL_NAME, gemini_configured=bool(settings.GEMINI_API_KEY))


def _closed(detail: str) -> TurnResponse:
    return TurnResponse(kind="closed", reply=detail, screening={"status": "Closed", "isComplete": False})


@router.post("/screening/start/{acceptanceId}", dependencies=protected)
async def start_screening(acceptanceId: str, db: Session = Depends(get_database_session)):
    """Opens (or resumes) the interview when a donor accepts a request (Supervisor DonorAccepted workflow)."""
    try:
        session, _ = await screening_service.get_or_start(db, acceptanceId)
        profile = await screening_service.profile(session)
        return {"session_id": session.session_id, "acceptance_id": acceptanceId, "status": session.status,
                "screening": screening_service.progress(session, profile)}
    except ScreeningClosedError as ex:
        raise HTTPException(status_code=status.HTTP_409_CONFLICT, detail=str(ex))
    except BackendUnavailableError as ex:
        raise HTTPException(status_code=status.HTTP_503_SERVICE_UNAVAILABLE, detail=str(ex))


@router.get("/screening/session/{acceptanceId}", response_model=TurnResponse, dependencies=protected)
async def resume_session(acceptanceId: str, db: Session = Depends(get_database_session)):
    """Current question, progress and transcript (confidential answers are left out of the transcript)."""
    try:
        session, _ = await screening_service.get_or_start(db, acceptanceId)
        profile = await screening_service.profile(session)
    except ScreeningClosedError as ex:
        return _closed(str(ex))
    except BackendUnavailableError as ex:
        raise HTTPException(status_code=status.HTTP_503_SERVICE_UNAVAILABLE, detail=str(ex))

    progress = screening_service.progress(session, profile, include_transcript=True)
    question, previous = screening_service.current_question(session, profile)
    if session.status == "Submitted":
        reply = "Your screening report has been submitted to the doctor. You can follow the decision in My Acceptances."
    elif question is None:
        reply = "All your answers are saved, but the report has not reached the doctor yet. Send any message to submit it."
    else:
        intro = ("Hello! I'll ask you the blood donor screening questions one at a time. Ask me what any question or term "
                 "means whenever you like. " if progress["answered"] == 0 else "Welcome back. ")
        reply = intro + (f"Section {question.section_index} of 12: {question.section}. "
                         f"{render_question(question, profile, previous)['text']}")
    return TurnResponse(kind="resume", reply=reply, screening=progress)


@router.post("/screening/turn/{acceptanceId}", response_model=TurnResponse, dependencies=protected)
async def screening_turn(acceptanceId: str, request: TurnRequest, db: Session = Depends(get_database_session)):
    """One interview turn: records an answer, explains a question, or asks the donor to clarify."""
    try:
        state = await screening_turn_graph.ainvoke({"db": db, "acceptance_id": acceptanceId, "message": request.message})
    except ScreeningClosedError as ex:
        return _closed(str(ex))
    except BackendUnavailableError as ex:
        raise HTTPException(status_code=status.HTTP_503_SERVICE_UNAVAILABLE, detail=str(ex))

    return TurnResponse(kind=state.get("kind", "clarify"), reply=state.get("reply", ""), query=state.get("query"),
                        screening=screening_service.progress(state["session"], state["profile"]))


@router.get("/report/{acceptanceId}", dependencies=protected)
async def latest_report(acceptanceId: str, db: Session = Depends(get_database_session)):
    """Latest submitted report version (the backend holds the official, immutable copy of every version)."""
    report = (db.query(DonorScreeningReport).filter(DonorScreeningReport.acceptance_id == acceptanceId)
              .order_by(DonorScreeningReport.report_version.desc()).first())
    if not report or not report.report_json:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="No submitted report for this acceptance.")
    return json.loads(report.report_json)
