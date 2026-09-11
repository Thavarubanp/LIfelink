from typing import TypedDict, List, Dict, Any, Optional

class ScreeningAgentState(TypedDict):
    acceptance_id: str
    session_id: Optional[str]
    donor_user_id: Optional[str]
    blood_request_id: Optional[str]
    donor_profile: Dict[str, Any]
    blood_request: Dict[str, Any]
    answers: List[Dict[str, Any]]
    current_question_index: int
    validation_passed: bool
    analysis_result: Dict[str, Any]
    risk_level: str
    recommendation: str
    report_id: Optional[str]
    report_payload: Dict[str, Any]
    status: str
