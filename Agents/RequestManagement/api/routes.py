import json
import logging

from fastapi import APIRouter, BackgroundTasks, Depends, HTTPException, status
from sqlalchemy.orm import Session

from api.dependencies import get_database_session
from api.security import require_internal_key
from config.settings import settings
from database.db import SessionLocal
from database.models import DonorScreeningReport, ScreeningSession
from graph.workflow import screening_turn_graph
from models.api_models import AnswersRequest, HealthCheckResponse, TurnRequest, TurnResponse
from models.donor_screening import QUESTIONS
from services.backend_client import BackendUnavailableError
from services.backend_client import backend_client
from services.screening_service import CONFIRM, ScreeningClosedError, ScreeningPausedError, screening_service

logger = logging.getLogger("ScreeningAgentRoutes")
router = APIRouter(prefix="/api/agent", tags=["Request Management Agent (Donor Screening)"])
protected = [Depends(require_internal_key)]


@router.get("/health", response_model=HealthCheckResponse)
def health_check():
    return HealthCheckResponse(status="Healthy", agent="LifeLink Request Management Agent", framework="FastAPI + LangGraph + Gemini",
                               model=settings.MODEL_NAME, gemini_configured=bool(settings.GEMINI_API_KEY))


def _closed(detail: str, paused: bool = False) -> TurnResponse:
    # Paused = the admin suspended the request; the answers are kept and the interview resumes after the lift
    return TurnResponse(kind="paused" if paused else "closed", reply=detail,
                        screening={"status": "Paused" if paused else "Closed", "isComplete": False})


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
        return _closed(str(ex), paused=isinstance(ex, ScreeningPausedError))
    except BackendUnavailableError as ex:
        raise HTTPException(status_code=status.HTTP_503_SERVICE_UNAVAILABLE, detail=str(ex))

    progress = screening_service.progress(session, profile, include_transcript=True)
    question = screening_service.current_question(session, profile)
    if session.status == "Submitted":
        reply = "Your screening answers have been sent to the doctor. You can follow the decision in My Acceptances."
    elif question is None:
        reply = "All your answers are saved and confirmed, but they have not reached the doctor yet. Send any message to submit them."
    elif question == CONFIRM:
        reply = "Welcome back. That's all 7 questions. Please tick \"I confirm my answers are true\" to send your answers to the doctor."
    else:
        intro = (f"Hello! I'll ask you {len(QUESTIONS)} short screening questions. Use the options in each question or type your "
                 "answer, and ask me what anything means whenever you like. " if progress["answered"] == 0 else "Welcome back. ")
        reply = intro + f"Question {question.number} of {len(QUESTIONS)}: {question.text}"
    return TurnResponse(kind="resume", reply=reply, screening=progress)


@router.post("/screening/turn/{acceptanceId}", response_model=TurnResponse, dependencies=protected)
async def screening_turn(acceptanceId: str, request: TurnRequest, db: Session = Depends(get_database_session)):
    """One interview turn: records an answer, explains a question, or asks the donor to clarify."""
    try:
        state = await screening_turn_graph.ainvoke({"db": db, "acceptance_id": acceptanceId, "message": request.message,
                                                    "structured": request.structured or {}})
    except ScreeningClosedError as ex:
        return _closed(str(ex), paused=isinstance(ex, ScreeningPausedError))
    except BackendUnavailableError as ex:
        raise HTTPException(status_code=status.HTTP_503_SERVICE_UNAVAILABLE, detail=str(ex))

    return TurnResponse(kind=state.get("kind", "clarify"), reply=state.get("reply", ""), query=state.get("query"),
                        screening=screening_service.progress(state["session"], state["profile"], follow_up=bool(state.get("follow_up"))))


async def _load_for_form(acceptance_id: str, db: Session):
    session = db.query(ScreeningSession).filter(ScreeningSession.acceptance_id == acceptance_id).first()
    if session is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="No screening for this acceptance.")
    try:
        profile = await screening_service.profile(session)
    except BackendUnavailableError as ex:
        raise HTTPException(status_code=status.HTTP_503_SERVICE_UNAVAILABLE, detail=str(ex))
    return session, profile


@router.post("/screening/answers/{acceptanceId}/validate", dependencies=protected)
async def validate_answers(acceptanceId: str, request: AnswersRequest, db: Session = Depends(get_database_session)):
    """Checks a full set of answers from the edit form (7.2) without saving anything: {valid, errors}."""
    _, profile = await _load_for_form(acceptanceId, db)
    _, errors = screening_service.validate_full(request.answers, profile)
    return {"valid": not errors, "errors": errors}


async def _resubmit_in_background(acceptance_id: str, answers: dict):
    """Re-checks the edited answers and submits the new report version (the chat interview does not run again)."""
    db = SessionLocal()
    try:
        session = db.query(ScreeningSession).filter(ScreeningSession.acceptance_id == acceptance_id).first()
        profile = await screening_service.profile(session)
        clean, errors = screening_service.validate_full(answers, profile)
        if errors:
            logger.warning("Edited answers for %s were no longer valid: %s", acceptance_id, errors)
            return
        acceptance = await backend_client.get_acceptance(acceptance_id)
        screening_service.replace_answers(db, session, clean)
        await screening_service.submit(db, session, acceptance)
    except Exception as ex:  # the donor can still finish in the chat ("Continue screening"); the answers are kept
        logger.error("Background resubmission for %s failed (%s).", acceptance_id, type(ex).__name__)
    finally:
        db.close()


@router.put("/screening/answers/{acceptanceId}", status_code=status.HTTP_202_ACCEPTED, dependencies=protected)
async def replace_answers(acceptanceId: str, request: AnswersRequest, background: BackgroundTasks, db: Session = Depends(get_database_session)):
    """The donor saved the edit form (the backend has already set the acceptance back to ScreeningPending): the answers are
    validated now, and the new report version is built and submitted in the background."""
    _, profile = await _load_for_form(acceptanceId, db)
    _, errors = screening_service.validate_full(request.answers, profile)
    if errors:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="; ".join(errors))
    background.add_task(_resubmit_in_background, acceptanceId, dict(request.answers))
    return {"accepted": True}


@router.get("/report/{acceptanceId}", dependencies=protected)
async def latest_report(acceptanceId: str, db: Session = Depends(get_database_session)):
    """Latest submitted report version (the backend holds the official, immutable copy of every version)."""
    report = (db.query(DonorScreeningReport).filter(DonorScreeningReport.acceptance_id == acceptanceId)
              .order_by(DonorScreeningReport.report_version.desc()).first())
    if not report or not report.report_json:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="No submitted report for this acceptance.")
    return json.loads(report.report_json)
