from langgraph.graph import StateGraph, START, END
from models import NotificationAgentState
from nodes import (
    check_priority_node,
    find_eligible_donors_node,
    rank_donors_node,
    generate_notifications_node
)

def create_notification_agent_graph():
    """
    Builds the LangGraph Workflow for Student 2:
    START -> CheckPriority -> FindEligibleDonors -> RankDonors -> GenerateNotifications -> END
    """
    workflow = StateGraph(NotificationAgentState)

    # Add Nodes
    workflow.add_node("check_priority", check_priority_node)
    workflow.add_node("find_eligible_donors", find_eligible_donors_node)
    workflow.add_node("rank_donors", rank_donors_node)
    workflow.add_node("generate_notifications", generate_notifications_node)

    # Add Edges
    workflow.add_edge(START, "check_priority")
    workflow.add_edge("check_priority", "find_eligible_donors")
    workflow.add_edge("find_eligible_donors", "rank_donors")
    workflow.add_edge("rank_donors", "generate_notifications")
    workflow.add_edge("generate_notifications", END)

    return workflow.compile()

# Pre-compiled graph instance
notification_agent_app = create_notification_agent_graph()
