"""Versioned immutable screening reports, reopenable sessions, doctor review moved to the backend

Revision ID: 002_versioned_reports
Revises: 001_initial_screening_agent_tables
Create Date: 2026-09-25 10:00:00.000000

The doctor's decision now lives in the LifeLink backend (DonorVerifications), so the agent's own
doctor_reviews table is dropped. Sessions from the old 12-section questionnaire use different question IDs;
they are marked Legacy so donors start the new interview.
"""
from typing import Sequence, Union
from alembic import op
import sqlalchemy as sa

revision: str = "002_versioned_reports"
down_revision: Union[str, None] = "001_initial_screening_agent_tables"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    with op.batch_alter_table("screening_sessions") as batch:
        batch.add_column(sa.Column("revision", sa.Integer(), nullable=False, server_default="1"))
        batch.add_column(sa.Column("previous_answers", sa.Text(), nullable=True))
    op.execute("UPDATE screening_sessions SET status = 'Legacy'")

    with op.batch_alter_table("donor_screening_reports") as batch:
        batch.add_column(sa.Column("report_version", sa.Integer(), nullable=False, server_default="1"))
        batch.add_column(sa.Column("report_json", sa.Text(), nullable=True))

    op.drop_table("doctor_reviews")


def downgrade() -> None:
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
    with op.batch_alter_table("donor_screening_reports") as batch:
        batch.drop_column("report_json")
        batch.drop_column("report_version")
    with op.batch_alter_table("screening_sessions") as batch:
        batch.drop_column("previous_answers")
        batch.drop_column("revision")
