from enum import Enum
from typing import Any, Dict
from pydantic import BaseModel, Field


class EventType(str, Enum):
    """Supported event types triggering Planning Agent workflows."""
    BLOOD_REQUEST_APPROVED = "BloodRequestApproved"
    DONOR_ACCEPTED = "DonorAccepted"
    EMERGENCY_SHORTAGE = "EmergencyShortage"


class PlanRequest(BaseModel):
    """Input payload submitted to the Planning Agent."""
    eventType: str = Field(
        ...,
        description="Event type name (e.g. BloodRequestApproved, DonorAccepted, EmergencyShortage)",
        examples=["BloodRequestApproved", "DonorAccepted", "EmergencyShortage"]
    )
    payload: Dict[str, Any] = Field(
        default_factory=dict,
        description="Contextual payload required by downstream agents (e.g. requestId, acceptanceId, hospitalId)"
    )
