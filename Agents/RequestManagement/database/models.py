import uuid
from datetime import datetime, timezone
from sqlalchemy import (
    Column,
    String,
    Integer,
    Float,
    DateTime,
    Text,
    ForeignKey,
    Index
)
from sqlalchemy.orm import relationship
from database.db import Base

def utc_now():
    return datetime.now(timezone.utc)

class ScreeningSession(Base):
    __tablename__ = "screening_sessions"

    session_id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    acceptance_id = Column(String(36), nullable=False, index=True)
    donor_user_id = Column(String(36), nullable=False, index=True)
    blood_request_id = Column(String(36), nullable=False, index=True)
    
    # Statuses: Pending, InProgress, Completed, UnderDoctorReview, DoctorApproved, DoctorRejected
    status = Column(String(50), nullable=False, default="Pending", index=True)
    current_step = Column(Integer, nullable=False, default=0)
    
    started_at = Column(DateTime, default=utc_now, nullable=False)
    completed_at = Column(DateTime, nullable=True)

    # Relationships
    answers = relationship("DonorScreeningAnswer", back_populates="session", cascade="all, delete-orphan", order_by="DonorScreeningAnswer.created_at")
    report = relationship("DonorScreeningReport", back_populates="session", uselist=False, cascade="all, delete-orphan")


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
    __tablename__ = "donor_screening_reports"

    report_id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    session_id = Column(String(36), ForeignKey("screening_sessions.session_id", ondelete="CASCADE"), nullable=False, index=True)
    acceptance_id = Column(String(36), nullable=False, index=True)
    donor_user_id = Column(String(36), nullable=False, index=True)
    blood_request_id = Column(String(36), nullable=False, index=True)

    # Risk Levels: LOW, MEDIUM, HIGH
    risk_level = Column(String(20), nullable=False, default="LOW", index=True)
    
    # Recommendations: Eligible, Temporarily Deferred, Requires Doctor Review
    recommendation = Column(String(50), nullable=False, default="Eligible", index=True)

    ai_summary = Column(Text, nullable=False)
    ai_analysis = Column(Text, nullable=False)  # Stored as JSON string
    ai_notes = Column(Text, nullable=True)

    created_at = Column(DateTime, default=utc_now, nullable=False)

    session = relationship("ScreeningSession", back_populates="report")
    review = relationship("DoctorReview", back_populates="report", uselist=False, cascade="all, delete-orphan")


class DoctorReview(Base):
    __tablename__ = "doctor_reviews"

    review_id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    report_id = Column(String(36), ForeignKey("donor_screening_reports.report_id", ondelete="CASCADE"), nullable=False, index=True)
    doctor_user_id = Column(String(36), nullable=False, index=True)

    # Physical screening vitals
    weight = Column(Float, nullable=True)
    blood_pressure = Column(String(20), nullable=True)
    pulse_rate = Column(Integer, nullable=True)
    temperature = Column(Float, nullable=True)
    hemoglobin = Column(Float, nullable=True)

    # Decision: Approved, Rejected, FurtherScreeningRequired
    decision = Column(String(50), nullable=False, index=True)
    doctor_notes = Column(Text, nullable=True)
    reviewed_at = Column(DateTime, default=utc_now, nullable=False)

    report = relationship("DonorScreeningReport", back_populates="review")
