from typing import List, Optional, Dict, Any
from pydantic import BaseModel, Field
from typing_extensions import TypedDict

class DonorCandidate(BaseModel):
    user_id: str
    full_name: str
    blood_group: str
    location: Optional[str] = "Nearby"
    last_donation_date: Optional[str] = None
    account_status: Optional[str] = "Active"
    contact_number: Optional[str] = None

class RankedDonor(BaseModel):
    user_id: str
    rank: int
    suitability_score: int = Field(ge=0, le=100)
    reason: str

class NotificationItem(BaseModel):
    recipient_type: str # 'Donor', 'Hospital'
    recipient_id: Optional[str] = None
    title: str
    message: str
    email_subject: Optional[str] = None
    email_body: Optional[str] = None
    sms_body: Optional[str] = None

class ProcessBloodRequestInput(BaseModel):
    request_id: str
    blood_group: str
    units_required: int = 1
    priority: str = "Normal" # Normal, High, Critical
    hospital_id: Optional[str] = None
    hospital_name: Optional[str] = "Partner Hospital"
    patient_reason: Optional[str] = "Clinical transfusion need"
    available_donors: List[DonorCandidate] = []
    verified_hospital_ids: List[str] = []

class RankDonorsInput(BaseModel):
    blood_group: str
    priority: str = "Normal"
    hospital_name: Optional[str] = "Hospital"
    eligible_donors: List[DonorCandidate]

class GenerateNotificationsInput(BaseModel):
    request_id: str
    blood_group: str
    units_required: int = 1
    priority: str = "Normal"
    hospital_name: str = "Partner Hospital"
    patient_reason: Optional[str] = "Clinical transfusion"
    eligible_donors: List[DonorCandidate] = []
    verified_hospital_ids: List[str] = []

class AgentProcessResponse(BaseModel):
    request_id: str
    priority: str
    is_urgent: bool
    eligible_donors_count: int
    ranked_donors: List[RankedDonor]
    notifications: List[NotificationItem]

# LangGraph Agent State
class NotificationAgentState(TypedDict):
    request_id: str
    blood_group: str
    units_required: int
    priority: str
    hospital_id: Optional[str]
    hospital_name: str
    patient_reason: str
    candidate_donors: List[Dict[str, Any]]
    eligible_donors: List[Dict[str, Any]]
    ranked_donors: List[Dict[str, Any]]
    verified_hospital_ids: List[str]
    is_urgent: bool
    notifications: List[Dict[str, Any]]
