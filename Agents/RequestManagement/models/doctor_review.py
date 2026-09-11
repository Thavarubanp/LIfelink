from typing import Optional
from datetime import datetime
from pydantic import BaseModel, Field

class DoctorReviewSubmission(BaseModel):
    doctor_user_id: str
    weight: Optional[float] = Field(None, ge=30.0, le=300.0, description="Weight in kg (minimum 45kg usually required for donation)")
    blood_pressure: Optional[str] = Field(None, pattern=r"^\d{2,3}/\d{2,3}$", description="e.g. 120/80")
    pulse_rate: Optional[int] = Field(None, ge=40, le=160, description="Heart rate in bpm")
    temperature: Optional[float] = Field(None, ge=35.0, le=42.0, description="Body temperature in Celsius")
    hemoglobin: Optional[float] = Field(None, ge=5.0, le=22.0, description="Hemoglobin in g/dL (usually >= 12.5 required)")
    decision: str = Field(..., pattern="^(Approved|Rejected|FurtherScreeningRequired)$")
    doctor_notes: Optional[str] = Field(None, max_length=1500)

class DoctorReviewResponse(BaseModel):
    review_id: str
    report_id: str
    doctor_user_id: str
    weight: Optional[float]
    blood_pressure: Optional[str]
    pulse_rate: Optional[int]
    temperature: Optional[float]
    hemoglobin: Optional[float]
    decision: str
    doctor_notes: Optional[str]
    reviewed_at: datetime
