from langgraph.graph import StateGraph, START, END
from graph.state import ScreeningAgentState
from graph.nodes import (
    load_acceptance_data_node,
    create_session_node,
    ask_questions_node,
    collect_answers_node,
    validate_answers_node,
    gemini_analysis_node,
    risk_classification_node,
    generate_report_node,
    store_report_node,
    mark_completed_node,
    send_to_doctor_review_queue_node
)

def create_screening_agent_graph():
    """
    Builds the complete LangGraph Workflow for Student 1:
    START
    -> Load Acceptance Data
    -> Create Session
    -> Ask Questions
    -> Collect Answers
    -> Validate Answers
    -> Gemini Analysis
    -> Risk Classification
    -> Generate Report
    -> Store Report
    -> Mark Completed
    -> Send To Doctor Review Queue
    -> END
    """
    workflow = StateGraph(ScreeningAgentState)

    # 1. Add All 11 Nodes
    workflow.add_node("load_acceptance_data", load_acceptance_data_node)
    workflow.add_node("create_session", create_session_node)
    workflow.add_node("ask_questions", ask_questions_node)
    workflow.add_node("collect_answers", collect_answers_node)
    workflow.add_node("validate_answers", validate_answers_node)
    workflow.add_node("gemini_analysis", gemini_analysis_node)
    workflow.add_node("risk_classification", risk_classification_node)
    workflow.add_node("generate_report", generate_report_node)
    workflow.add_node("store_report", store_report_node)
    workflow.add_node("mark_completed", mark_completed_node)
    workflow.add_node("send_to_doctor_review_queue", send_to_doctor_review_queue_node)

    # 2. Add Sequential Edges
    workflow.add_edge(START, "load_acceptance_data")
    workflow.add_edge("load_acceptance_data", "create_session")
    workflow.add_edge("create_session", "ask_questions")
    workflow.add_edge("ask_questions", "collect_answers")
    workflow.add_edge("collect_answers", "validate_answers")
    workflow.add_edge("validate_answers", "gemini_analysis")
    workflow.add_edge("gemini_analysis", "risk_classification")
    workflow.add_edge("risk_classification", "generate_report")
    workflow.add_edge("generate_report", "store_report")
    workflow.add_edge("store_report", "mark_completed")
    workflow.add_edge("mark_completed", "send_to_doctor_review_queue")
    workflow.add_edge("send_to_doctor_review_queue", END)

    return workflow.compile()

screening_agent_graph = create_screening_agent_graph()
