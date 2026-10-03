from typing import Any, Dict, Optional
from pydantic import BaseModel


class HealthCheckResponse(BaseModel):
    status: str
    agent: str
    framework: str
    model: str
    gemini_configured: bool


class TurnRequest(BaseModel):
    message: str = ""
    structured: Optional[Dict[str, Any]] = None  # values from the inputs inside the question bubble


class AnswersRequest(BaseModel):
    """A full set of answers from the edit form: field id -> value, plus CONFIRM_TRUE."""
    answers: Dict[str, Any]


class TurnResponse(BaseModel):
    """
    kind: answer (recorded; next question or a follow-up in reply) | question (donor asked something; `query` is for the
    Supervisor's knowledge base) | clarify | withdraw | complete | resume | closed | paused
    """
    kind: str
    reply: str
    query: Optional[str] = None
    screening: Dict[str, Any]
