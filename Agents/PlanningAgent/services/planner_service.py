import logging
from datetime import datetime, timezone
from typing import Any, Dict

from graph.state import AgentState
from models.request import PlanRequest
from models.response import PlanResponse, ExecutionPlan, ActionItem

logger = logging.getLogger("PlanningAgent.Service")


class PlannerService:
    """
    High-level orchestrator service invoking the LangGraph StateGraph
    and returning structured PlanResponse models.
    """

    async def generate_plan(self, request: PlanRequest) -> PlanResponse:
        """
        Executes the LangGraph planning workflow for the incoming event.
        """
        from graph.workflow import planning_agent_graph

        logger.info(f"Generating execution plan for eventType='{request.eventType}'")

        initial_state: AgentState = {
            "event_type": request.eventType,
            "payload": request.payload,
            "workflow_name": "",
            "agents_to_invoke": [],
            "agents_invoked": [],
            "screening_result": None,
            "matching_result": None,
            "inventory_result": None,
            "errors": [],
            "execution_plan": {},
            "status": "INIT"
        }

        try:
            final_state = await planning_agent_graph.ainvoke(initial_state)
        except Exception as ex:
            logger.error(f"Fatal error in planning agent StateGraph execution: {str(ex)}", exc_info=True)
            return PlanResponse(
                success=False,
                workflow=request.eventType,
                agentsInvoked=[],
                executionPlan=ExecutionPlan(
                    summary=f"Internal StateGraph execution failure: {str(ex)}",
                    workflowStatus="FAILED",
                    actionItems=[ActionItem(
                        agent="PlanningAgent",
                        action="EXECUTION_FAILED",
                        status="FAILED",
                        details={"error": str(ex)}
                    )],
                    results={}
                ),
                timestamp=datetime.now(timezone.utc).isoformat()
            )

        workflow_name = final_state.get("workflow_name", request.eventType)
        agents_invoked = final_state.get("agents_invoked", [])
        raw_plan = final_state.get("execution_plan", {})
        status = raw_plan.get("workflowStatus", "COMPLETED")

        action_items = [
            ActionItem(
                agent=item["agent"],
                action=item["action"],
                status=item["status"],
                details=item.get("details")
            )
            for item in raw_plan.get("actionItems", [])
        ]

        execution_plan = ExecutionPlan(
            summary=raw_plan.get("summary", "Workflow completed."),
            workflowStatus=status,
            actionItems=action_items,
            results=raw_plan.get("results", {})
        )

        return PlanResponse(
            success=(status == "COMPLETED"),
            workflow=workflow_name,
            agentsInvoked=agents_invoked,
            executionPlan=execution_plan,
            timestamp=datetime.now(timezone.utc).isoformat()
        )


planner_service = PlannerService()
