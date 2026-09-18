import logging
from datetime import datetime, timezone
from fastapi import APIRouter, HTTPException, status

from config.settings import settings
from models.request import PlanRequest
from models.response import PlanResponse, HealthResponse, AgentHealthStatus
from services.planner_service import planner_service
from services.agent_clients import agent_clients

logger = logging.getLogger("PlanningAgent.API")
router = APIRouter(tags=["LifeLink Planning Agent"])


@router.get("/health", response_model=HealthResponse)
async def health_check() -> HealthResponse:
    """
    Health check and connectivity diagnostic endpoint.
    Probes Agent 1, Agent 2, and Agent 3 endpoints.
    """
    logger.info("Health check requested.")
    downstream_health = await agent_clients.check_health()

    status_str = "Healthy"
    # If any downstream is unreachable, indicate degraded state
    if any(s.get("status") == "UNREACHABLE" for s in downstream_health.values()):
        status_str = "Degraded"

    mapped_downstream = {
        name: AgentHealthStatus(
            url=info["url"],
            status=info["status"],
            latencyMs=info.get("latencyMs"),
            error=info.get("error")
        )
        for name, info in downstream_health.items()
    }

    return HealthResponse(
        status=status_str,
        service="LifeLink Planning Agent (Workflow Orchestrator)",
        version=settings.VERSION,
        timestamp=datetime.now(timezone.utc).isoformat(),
        downstreamAgents=mapped_downstream
    )


@router.post("/plan", response_model=PlanResponse, status_code=status.HTTP_200_OK)
async def generate_plan(request: PlanRequest) -> PlanResponse:
    """
    Primary orchestration endpoint for the ASP.NET Core backend.
    Receives platform events, triggers the LangGraph StateGraph, and returns a unified execution plan.
    """
    if not request.eventType:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Missing required field 'eventType'."
        )

    return await planner_service.generate_plan(request)
