import json
import logging
import uuid
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional, Tuple

from sqlalchemy.orm import Session

from database.models import DonorScreeningAnswer, DonorScreeningReport, ScreeningSession
from models.donor_screening import SECTIONS, QuestionDefinition, get_applicable_questions, render_question
from services import eligibility_rules, report_service
from services.answer_parser import display_answer
from services.backend_client import backend_client
from services.gemini_service import gemini_service

logger = logging.getLogger("ScreeningService")

OPEN_ACCEPTANCE_STATUSES = ("Accepted", "ScreeningPending")


class ScreeningClosedError(Exception):
    """The donation is no longer in screening (withdrawn, decided, released or already submitted)."""


def utc_now():
    return datetime.now(timezone.utc)


def _profile_from_backend(data: Dict[str, Any]) -> Dict[str, Any]:
    dob = data.get("dateOfBirth")
    return {
        "fullName": data.get("fullName") or None,
        "dateOfBirth": str(dob)[:10] if dob else None,
        "gender": data.get("gender") or None,
        "address": data.get("address") or None,
        "phoneNumber": data.get("phoneNumber") or None,
        "email": data.get("email") or None,
        "bloodGroup": data.get("bloodGroup") or None,
    }


class ScreeningService:
    """Donor screening interview state: sessions, answers, progress, reopening and report submission."""

    async def get_or_start(self, db: Session, acceptance_id: str) -> Tuple[ScreeningSession, Dict[str, Any]]:
        acceptance = await backend_client.get_acceptance(acceptance_id)
        backend_status = acceptance.get("status")
        session = db.query(ScreeningSession).filter(ScreeningSession.acceptance_id == acceptance_id).first()

        if session is None:
            if backend_status not in OPEN_ACCEPTANCE_STATUSES:
                raise ScreeningClosedError(f"This donation is {backend_status}, so screening is closed.")
            session = ScreeningSession(acceptance_id=acceptance_id, donor_user_id=str(acceptance.get("donorUserId")),
                                       blood_request_id=str(acceptance.get("bloodRequestId")), status="InProgress")
            db.add(session)
            db.commit()
            if backend_status == "Accepted":
                await backend_client.mark_screening_started(acceptance_id)
            return session, acceptance

        if session.status == "Legacy":
            # Session from the previous questionnaire: start the new interview
            self._clear_answers(db, session)
            session.status = "InProgress"
            db.commit()
        elif session.status == "Submitted" and backend_status == "ScreeningPending":
            # The donor chose "Update my answers": keep the old answers as defaults and start a new version
            session.previous_answers = json.dumps(self.answer_map(session))
            self._clear_answers(db, session)
            session.revision += 1
            session.status = "InProgress"
            session.completed_at = None
            db.commit()

        if session.status == "InProgress" and backend_status not in OPEN_ACCEPTANCE_STATUSES:
            raise ScreeningClosedError(f"This donation is {backend_status}, so screening is closed.")
        return session, acceptance

    async def profile(self, session: ScreeningSession) -> Dict[str, Any]:
        return _profile_from_backend(await backend_client.get_donor_profile(session.donor_user_id))

    @staticmethod
    def _clear_answers(db: Session, session: ScreeningSession) -> None:
        for answer in list(session.answers):
            db.delete(answer)
        session.current_step = 0
        db.flush()
        db.refresh(session)

    @staticmethod
    def answer_map(session: ScreeningSession) -> Dict[str, str]:
        return {a.question_id: a.answer for a in session.answers}

    @staticmethod
    def previous_answers(session: ScreeningSession) -> Dict[str, str]:
        try:
            return json.loads(session.previous_answers) if session.previous_answers else {}
        except json.JSONDecodeError:
            return {}

    def current_question(self, session: ScreeningSession, profile: Dict[str, Any]) -> Tuple[Optional[QuestionDefinition], Optional[str]]:
        answers = self.answer_map(session)
        for q in get_applicable_questions(profile, answers):
            if q.question_id not in answers:
                previous = self.previous_answers(session).get(q.question_id)
                return q, previous
        return None, None

    def save_answer(self, db: Session, session: ScreeningSession, question: QuestionDefinition, value: str) -> None:
        existing = next((a for a in session.answers if a.question_id == question.question_id), None)
        if existing:
            existing.answer = value
            existing.created_at = utc_now()
        else:
            db.add(DonorScreeningAnswer(session_id=session.session_id, question_id=question.question_id,
                                        question_text=question.question_text, answer=value, created_at=utc_now()))
        session.current_step += 1
        db.commit()
        db.refresh(session)

    def progress(self, session: ScreeningSession, profile: Dict[str, Any], include_transcript: bool = False) -> Dict[str, Any]:
        answers = self.answer_map(session)
        applicable = get_applicable_questions(profile, answers)
        question, previous = self.current_question(session, profile)
        state: Dict[str, Any] = {
            "status": session.status,
            "isComplete": session.status == "Submitted",
            "section": question.section if question else None,
            "sectionIndex": question.section_index if question else len(SECTIONS),
            "sectionCount": len(SECTIONS),
            "answered": sum(1 for q in applicable if q.question_id in answers),
            "total": len(applicable),
            "question": render_question(question, profile, previous) if question and session.status == "InProgress" else None,
        }
        if include_transcript:
            state["transcript"] = [
                {"section": q.section, "question": render_question(q, profile)["text"], "answer": display_answer(q, answers[q.question_id])}
                for q in applicable if q.question_id in answers and not q.confidential]
        return state

    async def submit(self, db: Session, session: ScreeningSession, acceptance: Dict[str, Any]) -> Dict[str, Any]:
        """Evaluates the answers, builds the next report version and submits it to the backend."""
        answers = self.answer_map(session)
        evaluation = eligibility_rules.evaluate(answers)
        summary = await gemini_service.summarise(report_service.deidentified_answers(answers), evaluation)
        version = session.revision
        report_id = str(uuid.uuid4())
        report = report_service.build_report(report_id=report_id, acceptance=acceptance, version=version,
                                             answers=answers, evaluation=evaluation, summary=summary)

        await backend_client.submit_report({
            "reportId": report_id,
            "acceptanceId": session.acceptance_id,
            "riskLevel": evaluation["risk_level"],
            "recommendation": evaluation["recommendation"],
            "status": "SubmittedToDoctor",
            "summary": summary["summary"],
            "reportJson": report_service.to_json(report),
        })

        db.add(DonorScreeningReport(
            report_id=report_id, session_id=session.session_id, acceptance_id=session.acceptance_id,
            donor_user_id=session.donor_user_id, blood_request_id=session.blood_request_id, report_version=version,
            risk_level=evaluation["risk_level"], recommendation=evaluation["recommendation"], ai_summary=summary["summary"],
            ai_analysis=json.dumps(evaluation["flags"]), ai_notes=summary.get("doctor_notes"), report_json=report_service.to_json(report)))
        session.status = "Submitted"
        session.completed_at = utc_now()
        db.commit()
        logger.info("Screening report version %s submitted for acceptance %s", version, session.acceptance_id)
        return report


screening_service = ScreeningService()
