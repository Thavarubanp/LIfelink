import json
import logging
import uuid
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional, Tuple, Union

from sqlalchemy.orm import Session

from database.models import DonorScreeningAnswer, DonorScreeningReport, ScreeningSession
from models.donor_screening import (CONFIRM_ID, PARTS_BY_ID, QUESTIONS, QuestionDefinition, applicable_parts, missing_parts,
                                    part_applies, render_confirm, render_question)
from services import eligibility_rules, report_service
from services.answer_parser import display_value, parse_structured
from services.backend_client import backend_client
from services.gemini_service import gemini_service

logger = logging.getLogger("ScreeningService")

OPEN_ACCEPTANCE_STATUSES = ("Accepted", "ScreeningPending")
CONFIRM = "CONFIRM"  # the final "I confirm my answers are true" step (not counted as a question)


class ScreeningClosedError(Exception):
    """The donation is no longer in screening (withdrawn, decided, released or already submitted)."""


class ScreeningPausedError(ScreeningClosedError):
    """The admin has suspended the blood request: the interview pauses (answers are kept) until it is lifted."""


PAUSED_MESSAGE = ("This blood request is temporarily suspended by the administrator, so screening is paused. "
                  "Your answers so far are saved; you can continue when the suspension is lifted.")


def utc_now():
    return datetime.now(timezone.utc)


def _profile_from_backend(data: Dict[str, Any]) -> Dict[str, Any]:
    dob = data.get("dateOfBirth")
    return {
        "fullName": data.get("fullName") or None,
        "dateOfBirth": str(dob)[:10] if dob else None,
        "gender": data.get("gender") or None,
        "phoneNumber": data.get("phoneNumber") or None,
    }


def _is_current(question_id: str) -> bool:
    return question_id in PARTS_BY_ID or question_id == CONFIRM_ID


class ScreeningService:
    """Donor screening interview state: sessions, answers per field, follow-ups, form edits and report submission."""

    async def get_or_start(self, db: Session, acceptance_id: str) -> Tuple[ScreeningSession, Dict[str, Any]]:
        acceptance = await backend_client.get_acceptance(acceptance_id)
        if acceptance.get("requestSuspended"):
            raise ScreeningPausedError(PAUSED_MESSAGE)
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

        legacy = any(not _is_current(a.question_id) for a in session.answers)
        if session.status == "Legacy" or (legacy and session.status == "InProgress"):
            # A session from the earlier 12-section questionnaire: start the 7 questions (old answers are not reused)
            self._clear_answers(db, session)
            session.status = "InProgress"
            db.commit()
        elif session.status == "Submitted" and backend_status == "ScreeningPending":
            # The donor chose to update their answers in the chat: the old answers become the defaults of a new version
            session.previous_answers = json.dumps({k: v for k, v in self.answer_map(session).items() if k != CONFIRM_ID})
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

    def current_question(self, session: ScreeningSession, profile: Dict[str, Any]) -> Union[QuestionDefinition, str, None]:
        """The first question with a missing part, then the confirmation tick, then None (ready to submit)."""
        answers = self.answer_map(session)
        for q in QUESTIONS:
            if missing_parts(q, profile, answers):
                return q
        return CONFIRM if answers.get(CONFIRM_ID) != "Yes" else None

    def defaults(self, session: ScreeningSession, profile: Dict[str, Any], question: QuestionDefinition) -> Dict[str, Any]:
        """Values shown pre-filled for a question: the previous version's answers, else the donor's profile."""
        previous = self.previous_answers(session)
        result = {}
        for p in question.parts:
            value = previous.get(p.id) or (profile.get(p.prefill_key) if p.prefill_key else None)
            if value not in (None, ""):
                result[p.id] = value
        return result

    def save_values(self, db: Session, session: ScreeningSession, values: Dict[str, str], profile: Dict[str, Any]) -> None:
        existing = {a.question_id: a for a in session.answers}
        for field, value in values.items():
            label = PARTS_BY_ID[field].label if field in PARTS_BY_ID else "I confirm my answers are true"
            if field in existing:
                existing[field].answer = value
                existing[field].created_at = utc_now()
            else:
                db.add(DonorScreeningAnswer(session_id=session.session_id, question_id=field, question_text=label,
                                            answer=value, created_at=utc_now()))
        db.flush()
        db.refresh(session)
        # A follow-up that no longer applies (for example "No" to donated before) loses its answer
        answers = self.answer_map(session)
        for a in list(session.answers):
            part = PARTS_BY_ID.get(a.question_id)
            if part is not None and not part_applies(part, profile, answers):
                db.delete(a)
        session.current_step = sum(1 for q in QUESTIONS if not missing_parts(q, profile, self.answer_map(session)))
        db.commit()
        db.refresh(session)

    def render_current(self, session: ScreeningSession, profile: Dict[str, Any], follow_up: bool = False) -> Optional[Dict[str, Any]]:
        question = self.current_question(session, profile)
        if question is None:
            return None
        if question == CONFIRM:
            return render_confirm()
        return render_question(question, profile, self.answer_map(session), self.defaults(session, profile, question), follow_up)

    def progress(self, session: ScreeningSession, profile: Dict[str, Any], include_transcript: bool = False,
                 follow_up: bool = False) -> Dict[str, Any]:
        answers = self.answer_map(session)
        question = self.current_question(session, profile)
        answered = sum(1 for q in QUESTIONS if not missing_parts(q, profile, answers))
        number = question.number if isinstance(question, QuestionDefinition) else len(QUESTIONS)
        title = question.title if isinstance(question, QuestionDefinition) else ("Confirm" if question == CONFIRM else None)
        state: Dict[str, Any] = {
            "status": session.status,
            "isComplete": session.status == "Submitted",
            "section": title,
            "sectionIndex": number,
            "sectionCount": len(QUESTIONS),
            "answered": answered,
            "total": len(QUESTIONS),
            "question": self.render_current(session, profile, follow_up) if session.status == "InProgress" else None,
        }
        if include_transcript:
            transcript = []
            for q in QUESTIONS:
                parts = [p for p in applicable_parts(q, profile, answers) if p.id in answers]
                if not parts or missing_parts(q, profile, answers):
                    continue
                answer = "; ".join(f"{p.label}: {display_value(p, answers[p.id])}" for p in parts)
                transcript.append({"section": q.title, "question": f"Question {q.number} of {len(QUESTIONS)}: {q.text}", "answer": answer})
            state["transcript"] = transcript
        return state

    # ---------- the edit form (7.2): a full set of answers in one go, no chat ----------

    @staticmethod
    def validate_full(raw: Dict[str, Any], profile: Dict[str, Any]) -> Tuple[Dict[str, str], List[str]]:
        """Clean answers for all 7 questions, or the problems to fix. The confirmation tick is required."""
        clean: Dict[str, str] = {}
        errors: List[str] = []
        for q in QUESTIONS:
            values, problems = parse_structured(q, {k: v for k, v in raw.items() if k in {p.id for p in q.parts}})
            clean.update(values)
            errors.extend(f"Question {q.number}: {e}" for e in problems)
        # Keep only the parts that apply to these answers, then check that nothing required is missing
        clean = {k: v for k, v in clean.items() if part_applies(PARTS_BY_ID[k], profile, clean)}
        for q in QUESTIONS:
            for p in missing_parts(q, profile, clean):
                if not any(e.startswith(f"Question {q.number}: {p.label}") for e in errors):
                    errors.append(f"Question {q.number}: {p.label} is needed.")
        if str(raw.get(CONFIRM_ID, "")).lower() not in ("yes", "true"):
            errors.append("Please tick \"I confirm my answers are true\".")
        clean[CONFIRM_ID] = "Yes"
        return clean, errors

    def replace_answers(self, db: Session, session: ScreeningSession, clean: Dict[str, str]) -> None:
        """A new version from the edit form: the answers are replaced and the chat interview does not run again."""
        session.previous_answers = json.dumps({k: v for k, v in self.answer_map(session).items() if k != CONFIRM_ID})
        self._clear_answers(db, session)
        session.revision += 1
        session.status = "InProgress"
        session.completed_at = None
        for field, value in clean.items():
            label = PARTS_BY_ID[field].label if field in PARTS_BY_ID else "I confirm my answers are true"
            db.add(DonorScreeningAnswer(session_id=session.session_id, question_id=field, question_text=label, answer=value, created_at=utc_now()))
        session.current_step = len(QUESTIONS)
        db.commit()
        db.refresh(session)

    async def submit(self, db: Session, session: ScreeningSession, acceptance: Dict[str, Any]) -> Dict[str, Any]:
        """Evaluates the answers, builds the next report version and submits it to the backend."""
        answers = {k: v for k, v in self.answer_map(session).items() if k != CONFIRM_ID}
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
