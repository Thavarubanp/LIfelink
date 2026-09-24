import logging
from typing import Any, Dict

from graph.supervisor import supervisor_graph
from models.request import ChatRequest, PlanRequest
from models.response import (ActionItem, AgentNotification, ChatResponse, ExecutionPlan, PlanResponse,
                             ScreeningState, Segment, SuggestedAction)

logger = logging.getLogger("Supervisor.Service")

# The worker whose success decides whether an event workflow succeeded (the backend falls back otherwise)
REQUIRED_WORKER = {
    "BloodRequestApproved": "NotificationAgent",
    "DonorAccepted": "RequestManagementAgent",
    "EmergencyShortage": "NotificationAgent",
    "InventoryCheck": "NotificationAgent",
}


class SupervisorService:
    async def run_event(self, request: PlanRequest) -> PlanResponse:
        payload: Dict[str, Any] = dict(request.payload or {})
        for key in ("requestId", "bloodGroup", "hospitalId", "unitsRequired", "donorId"):
            value = getattr(request, key)
            if value is not None and key not in payload:
                payload[key] = value
        if request.urgency and "priority" not in payload:
            payload["priority"] = request.urgency

        try:
            state = await supervisor_graph.ainvoke({"kind": "event", "event_type": request.eventType, "payload": payload})
        except Exception as ex:
            logger.error("Supervisor event workflow failed: %s", ex, exc_info=True)
            return PlanResponse(success=False, eventType=request.eventType, workflow=request.eventType, workflowStatus="FAILED",
                                executionPlan=ExecutionPlan(summary="Supervisor workflow failed.", workflowStatus="FAILED"))

        results = state.get("results") or {}
        required = REQUIRED_WORKER.get(request.eventType)
        success = bool(required and (results.get(required) or {}).get("success"))
        any_failed = any(not r.get("success") for r in results.values())
        status = "FAILED" if not success else ("PARTIAL_FAILURE" if any_failed else "COMPLETED")

        action_items = [ActionItem(agent=agent, action=request.eventType, status="SUCCESS" if r.get("success") else "FAILED",
                                   details={"error": r.get("error")} if not r.get("success") else None)
                        for agent, r in results.items()]
        notifications = [AgentNotification(**n) for n in state.get("notifications") or [] if n.get("recipientId") and n.get("title")]
        summary = "; ".join(state.get("trace") or []) or "No workflow executed."
        return PlanResponse(
            success=success, eventType=request.eventType, workflow=request.eventType, workflowStatus=status,
            agentsInvoked=state.get("agents_invoked") or [],
            executionPlan=ExecutionPlan(summary=summary, workflowStatus=status, actionItems=action_items,
                                        results={k: v.get("data") for k, v in results.items() if v.get("success")}),
            executionTrace=state.get("trace") or [], notifications=notifications)

    async def run_chat(self, request: ChatRequest) -> ChatResponse:
        state = await supervisor_graph.ainvoke({"kind": "chat", "chat": request.model_dump()})
        screening = state.get("screening")
        return ChatResponse(
            reply=state.get("reply") or "",
            segments=[Segment(**s) for s in state.get("segments") or []],
            actions=[SuggestedAction(**a) for a in state.get("actions") or []],
            screening=ScreeningState(**screening) if screening else None)


supervisor_service = SupervisorService()
