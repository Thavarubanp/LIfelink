import uuid
from datetime import datetime, timezone
from sqlalchemy import Column, String, Integer, DateTime, Text, ForeignKey
from sqlalchemy.orm import relationship
from database.db import Base


def utc_now():
    return datetime.now(timezone.utc)


class ScreeningSession(Base):
    """
    Working state of one donor's screening interview (one per acceptance).
    Statuses: InProgress -> Submitted. When the donor chooses "Update my answers" the session reopens with the
    previous answers kept in previous_answers and revision increases; every submission is a new report version.
    """
    __tablename__ = "screening_sessions"

    session_id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    acceptance_id = Column(String(36), nullable=False, index=True, unique=True)
    donor_user_id = Column(String(36), nullable=False, index=True)
    blood_request_id = Column(String(36), nullable=False, index=True)
    status = Column(String(50), nullable=False, default="InProgress", index=True)
    current_step = Column(Integer, nullable=False, default=0)
    revision = Column(Integer, nullable=False, default=1)
    previous_answers = Column(Text, nullable=True)  # JSON {question_id: answer} from the superseded version
    started_at = Column(DateTime, default=utc_now, nullable=False)
    completed_at = Column(DateTime, nullable=True)

    answers = relationship("DonorScreeningAnswer", back_populates="session", cascade="all, delete-orphan", order_by="DonorScreeningAnswer.created_at")
    reports = relationship("DonorScreeningReport", back_populates="session", order_by="DonorScreeningReport.report_version")


class DonorScreeningAnswer(Base):
    __tablename__ = "donor_screening_answers"

    answer_id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    session_id = Column(String(36), ForeignKey("screening_sessions.session_id", ondelete="CASCADE"), nullable=False, index=True)
    question_id = Column(String(50), nullable=False, index=True)
    question_text = Column(Text, nullable=False)
    answer = Column(Text, nullable=False)
    created_at = Column(DateTime, default=utc_now, nullable=False)

    session = relationship("ScreeningSession", back_populates="answers")


class DonorScreeningReport(Base):
    """One submitted report version. Rows are only ever inserted, never updated (the backend keeps the official copy)."""
    __tablename__ = "donor_screening_reports"

    report_id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    session_id = Column(String(36), ForeignKey("screening_sessions.session_id", ondelete="CASCADE"), nullable=False, index=True)
    acceptance_id = Column(String(36), nullable=False, index=True)
    donor_user_id = Column(String(36), nullable=False, index=True)
    blood_request_id = Column(String(36), nullable=False, index=True)
    report_version = Column(Integer, nullable=False, default=1)
    risk_level = Column(String(20), nullable=False, default="LOW", index=True)
    recommendation = Column(String(50), nullable=False, default="Eligible", index=True)
    ai_summary = Column(Text, nullable=False)
    ai_analysis = Column(Text, nullable=False)  # rule flags as JSON
    ai_notes = Column(Text, nullable=True)
    report_json = Column(Text, nullable=True)   # the full report exactly as submitted
    created_at = Column(DateTime, default=utc_now, nullable=False)

    session = relationship("ScreeningSession", back_populates="reports")
