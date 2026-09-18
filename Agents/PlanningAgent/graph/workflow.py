import logging
from typing import Literal
from langgraph.graph import StateGraph, END

from graph.state import AgentState
from graph.nodes import (
    receive_request_node,
    determine_workflow_node,
    invoke_screening_agent_node,
    invoke_matching_agent_node,
    invoke_inventory_agent_node,
    aggregate_results_node,
    generate_execution_plan_node,
    return_response_node
)

logger = logging.getLogger("PlanningAgent.Workflow")


def route_workflow_decision(state: AgentState) -> Literal["invoke_matching_agent", "invoke_screening_agent", "invoke_inventory_agent", "generate_execution_plan"]:
    """
    Deterministic conditional router evaluating the workflow path.
    """
    workflow = state.get("workflow_name", "")

    if workflow == "BloodRequestApproved":
        return "invoke_matching_agent"
    elif workflow == "DonorAccepted":
        return "invoke_screening_agent"
    elif workflow == "EmergencyShortage":
        return "invoke_inventory_agent"
    else:
        return "generate_execution_plan"


def build_planning_graph() -> StateGraph:
    """
    Constructs the LangGraph StateGraph for the LifeLink Planning Agent.
    Implements deterministic sequencing for the 3 core healthcare workflows.
    """
    workflow = StateGraph(AgentState)

    # 1. Register all 8 execution nodes
    workflow.add_node("receive_request", receive_request_node)
    workflow.add_node("determine_workflow", determine_workflow_node)
    workflow.add_node("invoke_screening_agent", invoke_screening_agent_node)
    workflow.add_node("invoke_matching_agent", invoke_matching_agent_node)
    workflow.add_node("invoke_inventory_agent", invoke_inventory_agent_node)
    workflow.add_node("aggregate_results", aggregate_results_node)
    workflow.add_node("generate_execution_plan", generate_execution_plan_node)
    workflow.add_node("return_response", return_response_node)

    # 2. Set Entry Point
    workflow.set_entry_point("receive_request")

    # 3. Deterministic Pipeline Edges
    workflow.add_edge("receive_request", "determine_workflow")

    # 4. Conditional Edge from Workflow Determination
    workflow.add_conditional_edges(
        "determine_workflow",
        route_workflow_decision,
        {
            "invoke_matching_agent": "invoke_matching_agent",
            "invoke_screening_agent": "invoke_screening_agent",
            "invoke_inventory_agent": "invoke_inventory_agent",
            "generate_execution_plan": "generate_execution_plan"
        }
    )

    # 5. Workflow Specific Routing & Transitions:
    # - Workflow 1: BloodRequestApproved -> invoke_matching_agent -> aggregate_results
    # - Workflow 2: DonorAccepted -> invoke_screening_agent -> aggregate_results
    # - Workflow 3: EmergencyShortage -> invoke_inventory_agent -> invoke_matching_agent -> aggregate_results
    workflow.add_edge("invoke_screening_agent", "aggregate_results")
    workflow.add_edge("invoke_inventory_agent", "invoke_matching_agent")
    workflow.add_edge("invoke_matching_agent", "aggregate_results")

    # 6. Aggregation and Response Edges
    workflow.add_edge("aggregate_results", "generate_execution_plan")
    workflow.add_edge("generate_execution_plan", "return_response")
    workflow.add_edge("return_response", END)

    return workflow


# Compiled StateGraph instance ready for async invocation
planning_agent_graph = build_planning_graph().compile()
