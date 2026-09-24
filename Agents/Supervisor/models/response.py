from datetime import datetime, timezone
from typing import Any, Dict, List, Optional
from pydantic import BaseModel, Field


class ActionItem(BaseModel):
    agent: str
    action: str
    status: str  # SUCCESS, FAILED, SKIPPED
    details: Optional[Dict[str, Any]] = None


class ExecutionPlan(BaseModel):
    summary: str
    workflowStatus: str  # COMPLETED, PARTIAL_FAILURE, FAILED
    actionItems: List[ActionItem] = Field(default_factory=list)
    results: Dict[str, Any] = Field(default_factory=dict)


class AgentNotification(BaseModel):
    """An alert composed by the Notification agent; the backend saves it only for recipients it selected."""
    recipientType: str  # Donor or Hospital
    recipientId: str
    notificationType: str
    title: str
    message: str


class PlanResponse(BaseModel):
    success: bool
    eventType: str = ""
    workflow: str
    workflowStatus: str = ""
    agentsInvoked: List[str] = Field(default_factory=list)
    executionPlan: ExecutionPlan
    executionTrace: List[str] = Field(default_factory=list)
    notifications: List[AgentNotification] = Field(default_factory=list)
    timestamp: str = Field(default_factory=lambda: datetime.now(timezone.utc).isoformat())


class Source(BaseModel):
    title: str
    publisher: Optional[str] = None
    url: Optional[str] = None


class Segment(BaseModel):
    """
    One part of a reply, labelled by where it came from:
    medical = blood donation guidance (with sources), platform = LifeLink guide,
    account = the user's own data, screening = the donor screening interview.
    """
    type: str
    text: str
    sources: List[Source] = Field(default_factory=list)


class SuggestedAction(BaseModel):
    """A button the UI may show; the UI performs it with the user's own session (agents never act for users)."""
    label: str
    route: str


class ScreeningState(BaseModel):
    status: str = "InProgress"
    isComplete: bool = False
    section: Optional[str] = None
    sectionIndex: int = 0
    sectionCount: int = 12
    answered: int = 0
    total: int = 0
    question: Optional[Dict[str, Any]] = None
    transcript: List[Dict[str, Any]] = Field(default_factory=list)


class ChatResponse(BaseModel):
    reply: str
    segments: List[Segment] = Field(default_factory=list)
    actions: List[SuggestedAction] = Field(default_factory=list)
    screening: Optional[ScreeningState] = None


class AgentHealthStatus(BaseModel):
    url: str
    status: str
    latencyMs: Optional[float] = None
    error: Optional[str] = None


class HealthResponse(BaseModel):
    status: str
    service: str
    version: str
    timestamp: str = Field(default_factory=lambda: datetime.now(timezone.utc).isoformat())
    knowledge: Dict[str, Any] = Field(default_factory=dict)
    downstreamAgents: Dict[str, AgentHealthStatus] = Field(default_factory=dict)
