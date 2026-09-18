import logging
from datetime import datetime, timezone
from typing import Optional, Dict, Any, List, Tuple
from sqlalchemy.orm import Session

from database.models import ScreeningSession, DonorScreeningAnswer, DonorScreeningReport
from models.donor_screening import get_applicable_questions, QuestionDefinition
from services.backend_client import backend_client
from services.gemini_service import gemini_service
from services.report_service import report_service

logger = logging.getLogger("ScreeningService")

def utc_now():
    return datetime.now(timezone.utc)

class ScreeningService:
    """
    Orchestrates donor screening sessions, conversational progression,
    and transitions across statuses:
    Pending -> InProgress -> Completed -> UnderDoctorReview
    """

    async def start_screening_session(self, db: Session, acceptance_id: str) -> Tuple[ScreeningSession, QuestionDefinition, int]:
        """
        Creates or resumes a screening session for a given acceptanceId.
        Fetches donor and blood request metadata strictly via REST from the backend.
        """
        existing = db.query(ScreeningSession).filter(ScreeningSession.acceptance_id == acceptance_id).first()
        if existing:
            # Resume existing session
            questions = await self._get_applicable_questions_for_session(db, existing)
            answered_ids = {a.question_id for a in existing.answers}
            unanswered = [q for q in questions if q.question_id not in answered_ids]
            first_q = unanswered[0] if unanswered else questions[-1]
            return existing, first_q, len(questions)

        # Retrieve metadata from backend via REST API
        acceptance = await backend_client.get_acceptance(acceptance_id)
        blood_request_id = acceptance.get("bloodRequestId", "11111111-1111-1111-1111-111111111111")
        donor_user_id = acceptance.get("donorUserId", "22222222-2222-2222-2222-222222222222")

        session = ScreeningSession(
            acceptance_id=acceptance_id,
            donor_user_id=str(donor_user_id),
            blood_request_id=str(blood_request_id),
            status="InProgress",
            current_step=0,
            started_at=utc_now()
        )
        db.add(session)
        db.commit()
        db.refresh(session)

        # Notify backend of status change
        await backend_client.update_acceptance_status(acceptance_id, "ScreeningPending")

        # Determine questions
        questions = await self._get_applicable_questions_for_session(db, session)
        first_q = questions[0]
        return session, first_q, len(questions)

    async def get_session(self, db: Session, acceptance_id: str) -> Optional[ScreeningSession]:
        return db.query(ScreeningSession).filter(ScreeningSession.acceptance_id == acceptance_id).first()

    async def get_next_question(self, db: Session, acceptance_id: str) -> Tuple[Optional[QuestionDefinition], bool, int]:
        """
        Dynamically calculates remaining questions considering prior answers
        and conditional branches.
        """
        session = await self.get_session(db, acceptance_id)
        if not session:
            raise ValueError(f"Screening session for acceptance {acceptance_id} not found.")

        questions = await self._get_applicable_questions_for_session(db, session)
        answered_ids = {a.question_id for a in session.answers}
        unanswered = [q for q in questions if q.question_id not in answered_ids]

        if not unanswered:
            return None, True, 0

        return unanswered[0], False, len(unanswered)

    async def submit_answer(
        self,
        db: Session,
        acceptance_id: str,
        question_id: str,
        answer_text: str
    ) -> Tuple[ScreeningSession, bool, Optional[QuestionDefinition]]:
        """
        Persists an answer and dynamically yields the next applicable question.
        """
        session = await self.get_session(db, acceptance_id)
        if not session:
            raise ValueError(f"Screening session for acceptance {acceptance_id} not found.")

        if session.status in ["Completed", "UnderDoctorReview", "DoctorApproved", "DoctorRejected"]:
            raise ValueError(f"Session is already in finalized status '{session.status}'. Cannot submit further answers.")

        # Check if question was already answered, update or append
        existing_answer = db.query(DonorScreeningAnswer).filter(
            DonorScreeningAnswer.session_id == session.session_id,
            DonorScreeningAnswer.question_id == question_id
        ).first()

        # Find question text
        applicable_q = await self._get_applicable_questions_for_session(db, session)
        q_def = next((q for q in applicable_q if q.question_id == question_id), None)
        q_text = q_def.question_text if q_def else f"Question {question_id}"

        if existing_answer:
            existing_answer.answer = answer_text
            existing_answer.created_at = utc_now()
        else:
            answer_record = DonorScreeningAnswer(
                session_id=session.session_id,
                question_id=question_id,
                question_text=q_text,
                answer=answer_text,
                created_at=utc_now()
            )
            db.add(answer_record)

        session.current_step += 1
        db.commit()
        db.refresh(session)

        # Check next question
        next_q, is_complete, _ = await self.get_next_question(db, acceptance_id)
        return session, is_complete, next_q

    async def complete_screening(self, db: Session, acceptance_id: str) -> DonorScreeningReport:
        """
        Finalizes questionnaire interview, triggers risk analysis and report generation,
        saves report to database and disk, transitions status to UnderDoctorReview.
        """
        session = await self.get_session(db, acceptance_id)
        if not session:
            raise ValueError(f"Screening session for acceptance {acceptance_id} not found.")

        # Fetch donor profile and blood request context via backend REST client
        donor_profile = await backend_client.get_donor_profile(session.donor_user_id)
        blood_request = await backend_client.get_blood_request(session.blood_request_id)

        # Compile all answered questions
        answers_data = [
            {
                "question_id": a.question_id,
                "question_text": a.question_text,
                "answer": a.answer,
                "created_at": a.created_at.isoformat()
            }
            for a in session.answers
        ]

        # Execute AI analysis via Gemini / Clinical heuristics engine
        analysis = await gemini_service.analyze_screening(answers_data, donor_profile, blood_request)

        # Check existing report or create new
        report = db.query(DonorScreeningReport).filter(DonorScreeningReport.session_id == session.session_id).first()
        if not report:
            import json
            report = DonorScreeningReport(
                session_id=session.session_id,
                acceptance_id=session.acceptance_id,
                donor_user_id=session.donor_user_id,
                blood_request_id=session.blood_request_id,
                risk_level=analysis["risk_level"],
                recommendation=analysis["recommendation"],
                ai_summary=analysis["summary"],
                ai_analysis=json.dumps(analysis),
                ai_notes=analysis.get("notes", ""),
                created_at=utc_now()
            )
            db.add(report)
        else:
            import json
            report.risk_level = analysis["risk_level"]
            report.recommendation = analysis["recommendation"]
            report.ai_summary = analysis["summary"]
            report.ai_analysis = json.dumps(analysis)
            report.ai_notes = analysis.get("notes", "")

        # Transition status
        session.status = "UnderDoctorReview"
        session.completed_at = utc_now()
        db.commit()
        db.refresh(report)

        # Export full JSON report to reports/generated/report_{acceptanceId}.json
        await report_service.export_json_report(db, report)

        # Notify backend REST API
        await backend_client.update_acceptance_status(acceptance_id, "ScreeningCompleted")
        await backend_client.submit_report_metadata({
            "reportId": report.report_id,
            "acceptanceId": report.acceptance_id,
            "riskLevel": report.risk_level,
            "recommendation": report.recommendation,
            "status": "UnderDoctorReview"
        })

        return report

    async def _get_applicable_questions_for_session(self, db: Session, session: ScreeningSession) -> List[QuestionDefinition]:
        """Helper to get questions tailored to donor's gender and current branch answers."""
        donor_profile = await backend_client.get_donor_profile(session.donor_user_id)
        donor_gender = donor_profile.get("gender")
        
        current_answers = {a.question_id: a.answer for a in session.answers}
        return get_applicable_questions(donor_gender=donor_gender, current_answers=current_answers)

screening_service = ScreeningService()
