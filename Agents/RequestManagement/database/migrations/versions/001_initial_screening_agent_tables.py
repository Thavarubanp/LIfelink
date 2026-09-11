"""Initial screening agent tables

Revision ID: 001_initial_screening_agent_tables
Revises: 
Create Date: 2026-09-11 15:00:00.000000

"""
from typing import Sequence, Union
from alembic import op
import sqlalchemy as sa

revision: str = "001_initial_screening_agent_tables"
down_revision: Union[str, None] = None
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None

def upgrade() -> None:
    # 1. screening_sessions
    op.create_table(
        "screening_sessions",
        sa.Column("session_id", sa.String(length=36), primary_key=True),
        sa.Column("acceptance_id", sa.String(length=36), nullable=False),
        sa.Column("donor_user_id", sa.String(length=36), nullable=False),
        sa.Column("blood_request_id", sa.String(length=36), nullable=False),
        sa.Column("status", sa.String(length=50), nullable=False, server_default="Pending"),
        sa.Column("current_step", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("started_at", sa.DateTime(), nullable=False),
        sa.Column("completed_at", sa.DateTime(), nullable=True),
    )
    op.create_index("ix_screening_sessions_acceptance_id", "screening_sessions", ["acceptance_id"])
    op.create_index("ix_screening_sessions_donor_user_id", "screening_sessions", ["donor_user_id"])
    op.create_index("ix_screening_sessions_blood_request_id", "screening_sessions", ["blood_request_id"])
    op.create_index("ix_screening_sessions_status", "screening_sessions", ["status"])

    # 2. donor_screening_answers
    op.create_table(
        "donor_screening_answers",
        sa.Column("answer_id", sa.String(length=36), primary_key=True),
        sa.Column("session_id", sa.String(length=36), sa.ForeignKey("screening_sessions.session_id", ondelete="CASCADE"), nullable=False),
        sa.Column("question_id", sa.String(length=50), nullable=False),
        sa.Column("question_text", sa.Text(), nullable=False),
        sa.Column("answer", sa.Text(), nullable=False),
        sa.Column("created_at", sa.DateTime(), nullable=False),
    )
    op.create_index("ix_donor_screening_answers_session_id", "donor_screening_answers", ["session_id"])
    op.create_index("ix_donor_screening_answers_question_id", "donor_screening_answers", ["question_id"])

    # 3. donor_screening_reports
    op.create_table(
        "donor_screening_reports",
        sa.Column("report_id", sa.String(length=36), primary_key=True),
        sa.Column("session_id", sa.String(length=36), sa.ForeignKey("screening_sessions.session_id", ondelete="CASCADE"), nullable=False),
        sa.Column("acceptance_id", sa.String(length=36), nullable=False),
        sa.Column("donor_user_id", sa.String(length=36), nullable=False),
        sa.Column("blood_request_id", sa.String(length=36), nullable=False),
        sa.Column("risk_level", sa.String(length=20), nullable=False, server_default="LOW"),
        sa.Column("recommendation", sa.String(length=50), nullable=False, server_default="Eligible"),
        sa.Column("ai_summary", sa.Text(), nullable=False),
        sa.Column("ai_analysis", sa.Text(), nullable=False),
        sa.Column("ai_notes", sa.Text(), nullable=True),
        sa.Column("created_at", sa.DateTime(), nullable=False),
    )
    op.create_index("ix_donor_screening_reports_session_id", "donor_screening_reports", ["session_id"])
    op.create_index("ix_donor_screening_reports_acceptance_id", "donor_screening_reports", ["acceptance_id"])
    op.create_index("ix_donor_screening_reports_donor_user_id", "donor_screening_reports", ["donor_user_id"])
    op.create_index("ix_donor_screening_reports_blood_request_id", "donor_screening_reports", ["blood_request_id"])
    op.create_index("ix_donor_screening_reports_risk_level", "donor_screening_reports", ["risk_level"])
    op.create_index("ix_donor_screening_reports_recommendation", "donor_screening_reports", ["recommendation"])

    # 4. doctor_reviews
    op.create_table(
        "doctor_reviews",
        sa.Column("review_id", sa.String(length=36), primary_key=True),
        sa.Column("report_id", sa.String(length=36), sa.ForeignKey("donor_screening_reports.report_id", ondelete="CASCADE"), nullable=False),
        sa.Column("doctor_user_id", sa.String(length=36), nullable=False),
        sa.Column("weight", sa.Float(), nullable=True),
        sa.Column("blood_pressure", sa.String(length=20), nullable=True),
        sa.Column("pulse_rate", sa.Integer(), nullable=True),
        sa.Column("temperature", sa.Float(), nullable=True),
        sa.Column("hemoglobin", sa.Float(), nullable=True),
        sa.Column("decision", sa.String(length=50), nullable=False),
        sa.Column("doctor_notes", sa.Text(), nullable=True),
        sa.Column("reviewed_at", sa.DateTime(), nullable=False),
    )
    op.create_index("ix_doctor_reviews_report_id", "doctor_reviews", ["report_id"])
    op.create_index("ix_doctor_reviews_doctor_user_id", "doctor_reviews", ["doctor_user_id"])
    op.create_index("ix_doctor_reviews_decision", "doctor_reviews", ["decision"])

def downgrade() -> None:
    op.drop_table("doctor_reviews")
    op.drop_table("donor_screening_reports")
    op.drop_table("donor_screening_answers")
    op.drop_table("screening_sessions")
