from datetime import datetime, timezone
from typing import Any, Dict, List, Optional
from pydantic import BaseModel, Field


class ActionItem(BaseModel):
    """Specific action directive generated as part of the execution plan."""
    agent: str = Field(..., description="Target agent or system component")
    action: str = Field(..., description="Action to perform or result summary")
    status: str = Field(..., description="Status of the action: SUCCESS, FAILED, or PENDING")
    details: Optional[Dict[str, Any]] = Field(default=None, description="Detailed action payload or response")


class ExecutionPlan(BaseModel):
    """Aggregated execution plan returned to the ASP.NET Core backend."""
    summary: str = Field(..., description="Plain summary of the orchestrated execution")
    workflowStatus: str = Field(..., description="Overall workflow status: COMPLETED, PARTIAL_FAILURE, or FAILED")
    actionItems: List[ActionItem] = Field(default_factory=list, description="Ordered list of execution action items")
    results: Dict[str, Any] = Field(default_factory=dict, description="Consolidated raw outputs from invoked agents")


class PlanResponse(BaseModel):
    """Unified response envelope from the Planning Agent."""
    success: bool = Field(..., description="True if orchestration completed successfully")
    workflow: str = Field(..., description="Name of the executed workflow")
    agentsInvoked: List[str] = Field(default_factory=list, description="Names of downstream agents called")
    executionPlan: ExecutionPlan = Field(..., description="Unified execution plan and directives")
    timestamp: str = Field(
        default_factory=lambda: datetime.now(timezone.utc).isoformat(),
        description="ISO 8601 UTC timestamp of plan generation"
    )


class AgentHealthStatus(BaseModel):
    """Health check status for an individual downstream agent."""
    url: str
    status: str  # REACHABLE, UNREACHABLE, ERROR
    latencyMs: Optional[float] = None
    error: Optional[str] = None


class HealthResponse(BaseModel):
    """Planning Agent health diagnostic response."""
    status: str
    service: str
    version: str
    timestamp: str = Field(default_factory=lambda: datetime.now(timezone.utc).isoformat())
    downstreamAgents: Dict[str, AgentHealthStatus] = Field(default_factory=dict)
