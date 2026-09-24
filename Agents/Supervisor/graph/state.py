from typing import Any, Dict, List, Optional
from typing_extensions import TypedDict


class SupervisorState(TypedDict, total=False):
    """
    Shared state of the Supervisor graph. The supervisor node reads `queue` to decide which worker runs next;
    workers append their output and may push follow-up steps (for example the screening worker asks the
    knowledge step to explain a medical term, then the supervisor combines both answers).
    """
    kind: str                       # "event" or "chat"
    event_type: str
    payload: Dict[str, Any]
    chat: Dict[str, Any]            # ChatRequest as a dict (message, history, mode, acceptanceId, user, snapshot)
    intents: List[str]
    queue: List[str]
    next: str
    steps: int
    trace: List[str]
    agents_invoked: List[str]
    results: Dict[str, Any]
    errors: List[Dict[str, str]]
    knowledge_queries: List[Dict[str, str]]  # [{"domain": "medical" | "platform", "query": "..."}]
    segments: List[Dict[str, Any]]
    actions: List[Dict[str, str]]
    screening: Optional[Dict[str, Any]]
    pending_screening_reply: str   # screening question to repeat after a knowledge answer
    notifications: List[Dict[str, Any]]
    reply: str
