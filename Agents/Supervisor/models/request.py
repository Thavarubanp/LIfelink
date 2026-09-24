from enum import Enum
from typing import Any, Dict, List, Optional
from pydantic import BaseModel, Field


class EventType(str, Enum):
    """Platform events the backend sends to the Supervisor."""
    BLOOD_REQUEST_APPROVED = "BloodRequestApproved"
    DONOR_ACCEPTED = "DonorAccepted"
    EMERGENCY_SHORTAGE = "EmergencyShortage"
    INVENTORY_CHECK = "InventoryCheck"


class PlanRequest(BaseModel):
    """Event envelope sent by the ASP.NET Core backend (unchanged contract from the Planning Agent)."""
    eventType: str = Field(..., examples=["BloodRequestApproved", "DonorAccepted", "EmergencyShortage", "InventoryCheck"])
    requestId: Optional[str] = None
    bloodGroup: Optional[str] = None
    urgency: Optional[str] = None
    unitsRequired: Optional[int] = None
    hospitalId: Optional[str] = None
    donorId: Optional[str] = None
    payload: Dict[str, Any] = Field(default_factory=dict)


class ChatTurn(BaseModel):
    role: str  # "user" or "assistant"
    content: str


class ChatUser(BaseModel):
    role: str = "Donor"  # Donor, Doctor, HospitalStaff, Admin
    firstName: str = ""


class ChatRequest(BaseModel):
    """
    A chat message from the universal assistant, relayed by the backend. The snapshot is the only user data the
    Supervisor ever sees: the backend builds it for the signed-in user's role and nothing else.
    """
    mode: str = "assistant"  # "assistant" or "screening"
    message: str = ""
    history: List[ChatTurn] = Field(default_factory=list)
    acceptanceId: Optional[str] = None
    user: ChatUser = Field(default_factory=ChatUser)
    snapshot: Dict[str, Any] = Field(default_factory=dict)
