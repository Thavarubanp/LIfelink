from typing import List, Optional, Dict, Any
from datetime import datetime
from pydantic import BaseModel, Field
from models.donor_screening import QuestionDefinition
from models.report import FullDoctorScreeningReport
from models.doctor_review import DoctorReviewResponse

class HealthCheckResponse(BaseModel):
    status: str
    agent: str
    framework: str
    model: str
    gemini_configured: bool
    database_url: str

class StartScreeningResponse(BaseModel):
    session_id: str
    acceptance_id: str
    donor_user_id: str
    blood_request_id: str
    status: str
    total_estimated_questions: int
    first_question: QuestionDefinition

class SessionProgressResponse(BaseModel):
    session_id: str
    acceptance_id: str
    status: str
    current_step: int
    total_answered: int
    started_at: datetime
    completed_at: Optional[datetime] = None

class NextQuestionsResponse(BaseModel):
    session_id: str
    status: str
    next_question: Optional[QuestionDefinition] = None
    is_complete: bool
    remaining_count: int

class SubmitAnswerRequest(BaseModel):
    question_id: str
    answer: str

class SubmitAnswerResponse(BaseModel):
    session_id: str
    question_id: str
    status: str
    is_complete: bool
    next_question: Optional[QuestionDefinition] = None

class CompleteScreeningResponse(BaseModel):
    session_id: str
    report_id: str
    acceptance_id: str
    status: str
    risk_level: str
    recommendation: str
    summary: str
    flags: List[str]
    report_json_path: str
