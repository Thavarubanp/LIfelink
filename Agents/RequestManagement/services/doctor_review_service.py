import logging
from datetime import datetime, timezone
from typing import Optional
from sqlalchemy.orm import Session

from database.models import DoctorReview, DonorScreeningReport, ScreeningSession
from models.doctor_review import DoctorReviewSubmission, DoctorReviewResponse
from services.backend_client import backend_client
from services.report_service import report_service

logger = logging.getLogger("DoctorReviewService")

class DoctorReviewService:
    """
    Handles clinical physical screening recording, vital signs validation,
    and final medical approval or rejection by the attending doctor.
    """

    async def submit_doctor_review(
        self,
        db: Session,
        report_id: str,
        review_data: DoctorReviewSubmission
    ) -> DoctorReviewResponse:
        report = db.query(DonorScreeningReport).filter(DonorScreeningReport.report_id == report_id).first()
        if not report:
            raise ValueError(f"Screening report with ID {report_id} was not found.")

        # Check existing review or create
        existing_review = db.query(DoctorReview).filter(DoctorReview.report_id == report_id).first()
        now = datetime.now(timezone.utc)
        if existing_review:
            review = existing_review
            review.doctor_user_id = review_data.doctor_user_id
            review.weight = review_data.weight
            review.blood_pressure = review_data.blood_pressure
            review.pulse_rate = review_data.pulse_rate
            review.temperature = review_data.temperature
            review.hemoglobin = review_data.hemoglobin
            review.decision = review_data.decision
            review.doctor_notes = review_data.doctor_notes
            review.reviewed_at = now
        else:
            review = DoctorReview(
                report_id=report_id,
                doctor_user_id=review_data.doctor_user_id,
                weight=review_data.weight,
                blood_pressure=review_data.blood_pressure,
                pulse_rate=review_data.pulse_rate,
                temperature=review_data.temperature,
                hemoglobin=review_data.hemoglobin,
                decision=review_data.decision,
                doctor_notes=review_data.doctor_notes,
                reviewed_at=now
            )
            db.add(review)

        # Update screening session status
        session: ScreeningSession = report.session
        if review_data.decision == "Approved":
            session.status = "DoctorApproved"
            backend_status = "Verified"
        elif review_data.decision == "Rejected":
            session.status = "DoctorRejected"
            backend_status = "Rejected"
        else:
            session.status = "UnderDoctorReview"
            backend_status = "ScreeningCompleted"

        db.commit()
        db.refresh(review)

        # Refresh JSON export file on disk
        await report_service.export_json_report(db, report)

        # Notify backend API over REST
        await backend_client.update_acceptance_status(report.acceptance_id, backend_status)

        logger.info(
            f"Doctor {review_data.doctor_user_id} submitted review for report {report_id}: {review_data.decision}"
        )

        return DoctorReviewResponse(
            review_id=review.review_id,
            report_id=review.report_id,
            doctor_user_id=review.doctor_user_id,
            weight=review.weight,
            blood_pressure=review.blood_pressure,
            pulse_rate=review.pulse_rate,
            temperature=review.temperature,
            hemoglobin=review.hemoglobin,
            decision=review.decision,
            doctor_notes=review.doctor_notes,
            reviewed_at=review.reviewed_at
        )

    async def get_doctor_review(self, db: Session, report_id: str) -> Optional[DoctorReviewResponse]:
        review = db.query(DoctorReview).filter(DoctorReview.report_id == report_id).first()
        if not review:
            return None

        return DoctorReviewResponse(
            review_id=review.review_id,
            report_id=review.report_id,
            doctor_user_id=review.doctor_user_id,
            weight=review.weight,
            blood_pressure=review.blood_pressure,
            pulse_rate=review.pulse_rate,
            temperature=review.temperature,
            hemoglobin=review.hemoglobin,
            decision=review.decision,
            doctor_notes=review.doctor_notes,
            reviewed_at=review.reviewed_at
        )

doctor_review_service = DoctorReviewService()
