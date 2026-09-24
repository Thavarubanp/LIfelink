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


class TurnResponse(BaseModel):
    """
    kind: answer (recorded, next question in reply) | question (donor asked something; `query` is for the
    Supervisor's knowledge base) | clarify | withdraw | complete | resume | closed
    """
    kind: str
    reply: str
    query: Optional[str] = None
    screening: Dict[str, Any]
