from typing import List, Optional, Dict, Any
from datetime import datetime
from pydantic import BaseModel, Field

class DonorProfileInfo(BaseModel):
    user_id: str
    full_name: str
    email: str
    phone: Optional[str] = None
    nic: Optional[str] = None
    blood_group: Optional[str] = None
    gender: Optional[str] = None
    address: Optional[str] = None

class BloodRequestInfo(BaseModel):
    blood_request_id: str
    hospital_id: str
    hospital_name: Optional[str] = None
    patient_user_id: str
    patient_name: Optional[str] = None
    blood_group: str
    units_required: int
    priority: str
    reason: Optional[str] = None

class QuestionAnswerItem(BaseModel):
    question_id: str
    section: str
    question_text: str
    answer: str
    answered_at: datetime

class AiAnalysisDetail(BaseModel):
    summary: str
    findings: List[str] = []
    risks: List[str] = []
    flags: List[str] = []
    notes: Optional[str] = None

class PhysicalScreeningVitals(BaseModel):
    weight: Optional[float] = None
    blood_pressure: Optional[str] = None
    pulse_rate: Optional[int] = None
    temperature: Optional[float] = None
    hemoglobin: Optional[float] = None

class AuditInfo(BaseModel):
    acceptance_id: str
    blood_request_id: str
    donor_user_id: str
    doctor_user_id: Optional[str] = None
    session_id: str
    started_at: datetime
    completed_at: Optional[datetime] = None
    report_created_at: datetime
    reviewed_at: Optional[datetime] = None

class FullDoctorScreeningReport(BaseModel):
    report_id: str
    
    # SECTION A: Donor Information
    section_a_donor_info: DonorProfileInfo
    
    # SECTION B: Blood Request Information
    section_b_blood_request_info: BloodRequestInfo
    
    # SECTION C: Complete Questionnaire (Every single question and answer)
    section_c_questionnaire: List[QuestionAnswerItem]
    
    # SECTION D: AI Analysis
    section_d_ai_analysis: AiAnalysisDetail
    
    # SECTION E: Risk Level (LOW, MEDIUM, HIGH)
    section_e_risk_level: str
    
    # SECTION F: Recommendation (Eligible, Temporarily Deferred, Requires Doctor Review)
    section_f_recommendation: str
    
    # SECTION G: Physical Screening Placeholder (Completed by Doctor)
    section_g_physical_screening: Optional[PhysicalScreeningVitals] = None
    
    # SECTION H: Doctor Decision (Approved, Rejected, FurtherScreeningRequired)
    section_h_doctor_decision: Optional[str] = None
    
    # SECTION I: Doctor Notes
    section_i_doctor_notes: Optional[str] = None
    
    # SECTION J: Audit Information
    section_j_audit_info: AuditInfo
