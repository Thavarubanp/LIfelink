import logging

from fastapi import APIRouter, Depends, HTTPException, status

from api.security import require_internal_key
from config.settings import settings
from knowledge.store import knowledge_store
from models.request import ChatRequest, PlanRequest
from models.response import AgentHealthStatus, ChatResponse, HealthResponse, PlanResponse
from services.agent_clients import agent_clients
from services.supervisor_service import supervisor_service

logger = logging.getLogger("Supervisor.API")
router = APIRouter(tags=["LifeLink Supervisor"])


@router.get("/health", response_model=HealthResponse)
async def health_check() -> HealthResponse:
    downstream = await agent_clients.check_health()
    status_text = "Degraded" if any(d["status"] == "UNREACHABLE" for d in downstream.values()) else "Healthy"
    return HealthResponse(
        status=status_text,
        service="LifeLink Supervisor Agent",
        version=settings.VERSION,
        knowledge=knowledge_store.status(),
        downstreamAgents={name: AgentHealthStatus(**info) for name, info in downstream.items()})


@router.post("/plan", response_model=PlanResponse, dependencies=[Depends(require_internal_key)])
async def run_event(request: PlanRequest) -> PlanResponse:
    """Platform events from the backend (BloodRequestApproved, DonorAccepted, EmergencyShortage, InventoryCheck)."""
    if not request.eventType:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="Missing required field 'eventType'.")
    return await supervisor_service.run_event(request)


@router.post("/chat", response_model=ChatResponse, dependencies=[Depends(require_internal_key)])
async def chat(request: ChatRequest) -> ChatResponse:
    """Universal assistant and donor screening turns, relayed by the backend with a role-scoped snapshot."""
    if request.mode == "screening" and not request.acceptanceId:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="acceptanceId is required for screening.")
    return await supervisor_service.run_chat(request)
