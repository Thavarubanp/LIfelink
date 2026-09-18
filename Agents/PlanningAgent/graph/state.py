from typing import Any, Dict, List, Optional
from typing_extensions import TypedDict


class AgentState(TypedDict):
    """
    Internal State managed by the LangGraph Planning Agent StateGraph.
    Carries execution context across deterministic routing nodes.
    """
    event_type: str
    payload: Dict[str, Any]
    workflow_name: str
    agents_to_invoke: List[str]
    agents_invoked: List[str]
    screening_result: Optional[Dict[str, Any]]
    matching_result: Optional[Dict[str, Any]]
    inventory_result: Optional[Dict[str, Any]]
    errors: List[Dict[str, Any]]
    execution_plan: Dict[str, Any]
    status: str
