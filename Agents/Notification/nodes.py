import os
import json
import logging
from typing import Dict, Any, List
from dotenv import load_dotenv
from langchain_google_genai import ChatGoogleGenerativeAI
from langchain_core.messages import HumanMessage
from models import NotificationAgentState
from prompts import DONOR_RANKING_PROMPT, NOTIFICATIONS_GENERATION_PROMPT

load_dotenv()
logger = logging.getLogger("NotificationAgent")

# Blood Compatibility Table (Recipient -> Compatible Donor Blood Groups)
COMPATIBILITY_MATRIX: Dict[str, List[str]] = {
    "A+": ["A+", "A-", "O+", "O-"],
    "A-": ["A-", "O-"],
    "B+": ["B+", "B-", "O+", "O-"],
    "B-": ["B-", "O-"],
    "AB+": ["A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-"],
    "AB-": ["AB-", "A-", "B-", "O-"],
    "O+": ["O+", "O-"],
    "O-": ["O-"]
}

def get_llm():
    api_key = os.getenv("GOOGLE_API_KEY") or os.getenv("GEMINI_API_KEY")
    model_name = os.getenv("MODEL_NAME", "gemini-2.5-flash")
    if not api_key:
        logger.warning("GOOGLE_API_KEY is not set. Fallback templates will be used.")
        return None
    return ChatGoogleGenerativeAI(
        model=model_name,
        google_api_key=api_key,
        temperature=0.2
    )

def check_priority_node(state: NotificationAgentState) -> Dict[str, Any]:
    """Node 1: Evaluates request priority and sets urgency flag."""
    priority = (state.get("priority") or "Normal").strip().upper()
    is_urgent = priority in ["HIGH", "CRITICAL"]
    logger.info(f"CheckPriorityNode: Request {state.get('request_id')} Priority={priority}, IsUrgent={is_urgent}")
    return {"is_urgent": is_urgent}

def find_eligible_donors_node(state: NotificationAgentState) -> Dict[str, Any]:
    """
    Node 2: Filters candidate donors using strict business rules:
    - Compatible blood group
    - Active account status
    - Eligibility checks
    (NO Gemini used here - pure business logic)
    """
    target_blood_group = (state.get("blood_group") or "O+").strip().upper()
    candidates = state.get("candidate_donors") or []
    
    compatible_groups = COMPATIBILITY_MATRIX.get(target_blood_group, [target_blood_group])
    
    eligible = []
    for donor in candidates:
        donor_group = (donor.get("blood_group") or "").strip().upper()
        status = (donor.get("account_status") or "Active").strip().lower()
        
        # Check active status & blood compatibility
        if status == "active" and donor_group in compatible_groups:
            eligible.append(donor)
            
    logger.info(f"FindEligibleDonorsNode: Filtered {len(eligible)} eligible donors from {len(candidates)} candidates.")
    return {"eligible_donors": eligible}

def rank_donors_node(state: NotificationAgentState) -> Dict[str, Any]:
    """Node 3: Uses Google Gemini to score and rank pre-screened eligible donors."""
    eligible_donors = state.get("eligible_donors") or []
    if not eligible_donors:
        return {"ranked_donors": []}

    target_group = (state.get("blood_group") or "O+").strip().upper()
    priority = state.get("priority") or "Normal"
    hospital_name = state.get("hospital_name") or "Partner Hospital"

    llm = get_llm()
    if llm:
        try:
            donors_summary = [
                {
                    "user_id": d.get("user_id"),
                    "full_name": d.get("full_name"),
                    "blood_group": d.get("blood_group"),
                    "location": d.get("location", "Nearby"),
                    "last_donation_date": d.get("last_donation_date", "Never")
                }
                for d in eligible_donors
            ]
            
            prompt = DONOR_RANKING_PROMPT.format(
                blood_group=target_group,
                priority=priority,
                hospital_name=hospital_name,
                donors_json=json.dumps(donors_summary, indent=2)
            )
            
            response = llm.invoke([HumanMessage(content=prompt)])
            text = response.content.strip()
            
            # Clean markdown codeblocks if present
            if text.startswith("```json"):
                text = text[7:]
            elif text.startswith("```"):
                text = text[3:]
            if text.endswith("```"):
                text = text[:-3]
                
            ranked = json.loads(text.strip())
            logger.info(f"RankDonorsNode: Gemini successfully ranked {len(ranked)} donors.")
            return {"ranked_donors": ranked}
        except Exception as e:
            logger.warning(f"RankDonorsNode: Gemini call failed ({e}), falling back to deterministic ranking.")

    # Deterministic ranking fallback
    ranked = []
    sorted_donors = sorted(
        eligible_donors,
        key=lambda d: (
            d.get("blood_group", "").upper() == target_group,
            d.get("last_donation_date") is None
        ),
        reverse=True
    )
    for idx, d in enumerate(sorted_donors):
        is_exact = d.get("blood_group", "").upper() == target_group
        score = 95 - (idx * 5) if is_exact else 80 - (idx * 5)
        score = max(50, score)
        ranked.append({
            "user_id": d.get("user_id"),
            "rank": idx + 1,
            "suitability_score": score,
            "reason": "Exact blood group match and high readiness." if is_exact else "Compatible group donor available."
        })
    return {"ranked_donors": ranked}

def generate_notifications_node(state: NotificationAgentState) -> Dict[str, Any]:
    """Node 4: Uses Gemini to generate donor, hospital, and admin notifications based on priority rules."""
    priority = (state.get("priority") or "Normal").strip().upper()
    blood_group = state.get("blood_group") or "O+"
    units = state.get("units_required") or 1
    hospital_name = state.get("hospital_name") or "Partner Hospital"
    reason = state.get("patient_reason") or "Emergency medical treatment"
    req_id = state.get("request_id") or ""

    eligible_donors = state.get("eligible_donors") or []
    verified_hospital_ids = state.get("verified_hospital_ids") or []

    llm = get_llm()
    
    def generate_copy_for_role(role: str) -> Dict[str, str]:
        if llm:
            try:
                prompt = NOTIFICATIONS_GENERATION_PROMPT.format(
                    request_id=req_id,
                    blood_group=blood_group,
                    units_required=units,
                    priority=priority,
                    hospital_name=hospital_name,
                    patient_reason=reason,
                    recipient_role=role
                )
                res = llm.invoke([HumanMessage(content=prompt)])
                txt = res.content.strip()
                if txt.startswith("```json"):
                    txt = txt[7:]
                elif txt.startswith("```"):
                    txt = txt[3:]
                if txt.endswith("```"):
                    txt = txt[:-3]
                return json.loads(txt.strip())
            except Exception as e:
                logger.warning(f"GenerateNotificationsNode: AI copy generation failed for {role} ({e}). Using template.")
                
        # Template Fallback
        if role == "HospitalStaff":
            return {
                "title": f"[{priority}] Urgent Blood Shortage Notice ({blood_group})",
                "message": f"{hospital_name} requires {units} unit(s) of {blood_group} blood. Please verify available stock.",
                "email_subject": f"LifeLink Supply Notice: {blood_group} Needed at {hospital_name}",
                "email_body": f"Urgent shortage of {blood_group} blood ({units} units) logged by {hospital_name}.\nReason: {reason}.",
                "sms_body": f"[LifeLink] {priority}: {blood_group} ({units} units) needed at {hospital_name}."
            }
        else:
            return {
                "title": f"Urgent Need: {blood_group} Blood Needed at {hospital_name}",
                "message": f"A verified patient at {hospital_name} needs {blood_group} blood. Can you help save a life?",
                "email_subject": f"LifeLink: Urgent Blood Request for {blood_group}",
                "email_body": f"Dear LifeLink Donor,\n\n{hospital_name} has a verified need for {blood_group} blood.\nReason: {reason}.\nPlease open the app if you can donate.",
                "sms_body": f"LifeLink: Urgent need for {blood_group} blood at {hospital_name}. Please open the LifeLink app to respond."
            }

    notifications: List[Dict[str, Any]] = []

    # 1. Donor Notifications (Always sent to eligible donors)
    donor_copy = generate_copy_for_role("Donor")
    for donor in eligible_donors:
        notifications.append({
            "recipient_type": "Donor",
            "recipient_id": donor.get("user_id"),
            "title": donor_copy.get("title", ""),
            "message": donor_copy.get("message", ""),
            "email_subject": donor_copy.get("email_subject"),
            "email_body": donor_copy.get("email_body"),
            "sms_body": donor_copy.get("sms_body")
        })

    # 2. Hospital Notifications (Sent ONLY if High or Critical priority)
    if priority in ["HIGH", "CRITICAL"]:
        hospital_copy = generate_copy_for_role("HospitalStaff")
        for hid in verified_hospital_ids:
            notifications.append({
                "recipient_type": "Hospital",
                "recipient_id": hid,
                "title": hospital_copy.get("title", ""),
                "message": hospital_copy.get("message", ""),
                "email_subject": hospital_copy.get("email_subject"),
                "email_body": hospital_copy.get("email_body"),
                "sms_body": hospital_copy.get("sms_body")
            })

    logger.info(f"GenerateNotificationsNode: Generated {len(notifications)} total notifications.")
    return {"notifications": notifications}
