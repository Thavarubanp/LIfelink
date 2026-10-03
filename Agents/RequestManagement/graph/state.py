from typing import Any, Dict, List, Optional
from typing_extensions import TypedDict


class ScreeningTurnState(TypedDict, total=False):
    """State of one interview turn: the donor's message or structured answers in, the agent's reply and progress out."""
    db: Any
    acceptance_id: str
    message: str
    structured: Dict[str, Any]   # values from the inputs inside the question bubble
    session: Any
    acceptance: Dict[str, Any]
    profile: Dict[str, Any]
    question: Any                # QuestionDefinition, "CONFIRM" or None
    values: Dict[str, str]       # parsed answers to record
    errors: List[str]
    follow_up: bool              # the reply asks only for the missing parts of the same question
    hint: Optional[str]
    kind: str                    # answer | question | clarify | withdraw | submit | complete | closed | paused
    reply: str
    query: Optional[str]
