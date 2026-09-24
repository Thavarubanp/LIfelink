from typing import Any, Dict, Optional
from typing_extensions import TypedDict


class ScreeningTurnState(TypedDict, total=False):
    """State of one interview turn: the donor's message in, the agent's reply and progress out."""
    db: Any
    acceptance_id: str
    message: str
    session: Any
    acceptance: Dict[str, Any]
    profile: Dict[str, Any]
    question: Any
    previous: Optional[str]
    parsed: Any
    kind: str        # answer | question | clarify | withdraw | complete | closed
    reply: str
    query: Optional[str]
