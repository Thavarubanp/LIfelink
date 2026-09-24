import hmac
import json
import os
import uvicorn
import logging
from typing import Dict, List
from fastapi import Depends, FastAPI, Header, HTTPException, status
from dotenv import load_dotenv

from models import (
    ProcessBloodRequestInput,
    RankDonorsInput,
    GenerateNotificationsInput,
    AgentProcessResponse,
    RankedDonor,
    NotificationItem,
    NotificationAgentState,
    HospitalAlertsInput,
    HospitalAlertsResponse,
    HospitalNotification,
)
from graph import notification_agent_app
from nodes import rank_donors_node, generate_notifications_node, get_llm
from prompts import HOSPITAL_ALERT_PROMPT

load_dotenv()

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(name)s: %(message)s")
logger = logging.getLogger("LifeLinkAgentAPI")

INTERNAL_KEY = os.getenv("INTERNAL_SERVICE_API_KEY", "LifeLink-Internal-Agent-Key-2026")


async def require_internal_key(x_internal_key: str | None = Header(default=None)) -> None:
    """Only the Supervisor and the backend may call this agent (donor data is involved)."""
    if not INTERNAL_KEY or not x_internal_key or not hmac.compare_digest(x_internal_key, INTERNAL_KEY):
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Missing or invalid internal service key.")


# No CORS: browsers never call this agent directly.
app = FastAPI(
    title="LifeLink Notification Agent",
    version="3.0.0",
    description="Re-checks donor eligibility and composes donor and hospital notifications (LangGraph + Gemini)."
)
protected = [Depends(require_internal_key)]


@app.get("/health")
def health_check():
    api_key_configured = bool(os.getenv("GOOGLE_API_KEY") or os.getenv("GEMINI_API_KEY"))
    model_name = os.getenv("MODEL_NAME", "gemini-2.5-flash")
    return {
        "status": "Healthy",
        "agent": "LifeLink Notification Agent",
        "framework": "LangGraph + FastAPI",
        "model": model_name,
        "is_api_key_configured": api_key_configured
    }


@app.post("/process-request", response_model=AgentProcessResponse, dependencies=protected)
async def process_blood_request(input_data: ProcessBloodRequestInput):
    """
    Executes the full LangGraph Agent Workflow:
    CheckPriority -> FindEligibleDonors (re-checks every rule) -> RankDonors -> GenerateNotifications
    """
    try:
        initial_state: NotificationAgentState = {
            "request_id": input_data.request_id,
            "blood_group": input_data.blood_group,
            "units_required": input_data.units_required,
            "priority": input_data.priority,
            "hospital_id": input_data.hospital_id,
            "hospital_name": input_data.hospital_name or "Partner Hospital",
            "patient_reason": input_data.patient_reason or "Transfusion required",
            "candidate_donors": [d.model_dump() for d in input_data.available_donors],
            "eligible_donors": [],
            "ranked_donors": [],
            "verified_hospital_ids": input_data.verified_hospital_ids,
            "is_urgent": False,
            "notifications": []
        }

        final_state = await notification_agent_app.ainvoke(initial_state)

        ranked = [RankedDonor(**d) for d in final_state.get("ranked_donors", [])]
        notifications = [NotificationItem(**n) for n in final_state.get("notifications", [])]

        return AgentProcessResponse(
            request_id=input_data.request_id,
            priority=input_data.priority,
            is_urgent=final_state.get("is_urgent", False),
            eligible_donors_count=len(final_state.get("eligible_donors", [])),
            ranked_donors=ranked,
            notifications=notifications
        )
    except Exception as e:
        logger.error(f"Error executing agent workflow for request {input_data.request_id}: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail="Notification workflow failed.")


ALERT_TYPES = {
    "EmergencyStock": "EmergencyStockAlert",
    "InventoryShortage": "InventoryShortage",
    "PacketsExpiringSoon": "PacketsExpiringSoon",
    "TransferSuggestion": "TransferSuggestion",
}


def _template_alert(alert, context: Dict) -> Dict[str, str]:
    group = alert.blood_group or ""
    related = ", ".join(alert.related) if alert.related else ""
    if alert.kind == "EmergencyStock":
        requester = context.get("hospitalName") or "A partner hospital"
        return {"title": f"[{str(context.get('priority') or 'High').upper()}] Emergency Blood Support Needed ({group})",
                "message": alert.message or f"{requester} urgently needs {context.get('unitsRequired') or ''} unit(s) of {group}. "
                                            f"You hold {alert.units or 0} compatible unit(s); consider sending a transfer offer."}
    if alert.kind == "InventoryShortage":
        return {"title": f"{group} stock below threshold",
                "message": alert.message or f"Your {group} stock is below its threshold." + (f" Hospitals with spare stock: {related}. Consider a transfer request." if related else "")}
    if alert.kind == "PacketsExpiringSoon":
        return {"title": f"{group} packets expiring soon",
                "message": alert.message or f"{alert.units or 0} {group} packet(s) expire soon." + (f" {related} could use them; consider offering them before expiry." if related else " Use them first to prevent wastage.")}
    return {"title": f"Transfer suggestion ({group})", "message": alert.message or f"A transfer of {group} could balance stock with {related}."}


@app.post("/hospital-alerts", response_model=HospitalAlertsResponse, dependencies=protected)
async def hospital_alerts(input_data: HospitalAlertsInput):
    """
    Composes hospital alerts for the Supervisor (emergency support, shortages, expiring packets, transfer ideas).
    Facts come from the Inventory agent; Gemini only rewrites the wording, templates are used otherwise.
    """
    llm = get_llm()
    notifications: List[HospitalNotification] = []
    for alert in input_data.alerts[:50]:
        copy = _template_alert(alert, input_data.context)
        if llm:
            try:
                from langchain_core.messages import HumanMessage
                prompt = HOSPITAL_ALERT_PROMPT.format(kind=alert.kind, facts=copy["message"], title=copy["title"])
                text = llm.invoke([HumanMessage(content=prompt)]).content.strip().strip("`")
                if text.lower().startswith("json"):
                    text = text[4:]
                data = json.loads(text)
                if data.get("title") and data.get("message"):
                    copy = {"title": str(data["title"])[:120], "message": str(data["message"])[:600]}
            except Exception as ex:
                logger.warning("Hospital alert copy fell back to template (%s)", type(ex).__name__)
        notifications.append(HospitalNotification(recipient_id=alert.hospital_id,
                                                  notification_type=ALERT_TYPES.get(alert.kind, "InventoryAlert"), **copy))
    return HospitalAlertsResponse(notifications=notifications)


@app.post("/rank-donors", dependencies=protected)
async def rank_donors_endpoint(input_data: RankDonorsInput):
    """Direct standalone ranking of eligible donors using Gemini."""
    try:
        state: NotificationAgentState = {
            "request_id": "",
            "blood_group": input_data.blood_group,
            "units_required": 1,
            "priority": input_data.priority,
            "hospital_id": None,
            "hospital_name": input_data.hospital_name or "Hospital",
            "patient_reason": "",
            "candidate_donors": [],
            "eligible_donors": [d.model_dump() for d in input_data.eligible_donors],
            "ranked_donors": [],
            "verified_hospital_ids": [],
            "is_urgent": False,
            "notifications": []
        }
        res = rank_donors_node(state)
        return {"ranked_donors": res.get("ranked_donors", [])}
    except Exception as e:
        logger.error(f"Error in rank-donors endpoint: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail="Ranking failed.")


@app.post("/generate-notifications", dependencies=protected)
async def generate_notifications_endpoint(input_data: GenerateNotificationsInput):
    """Direct standalone generation of notifications using Gemini."""
    try:
        state: NotificationAgentState = {
            "request_id": input_data.request_id,
            "blood_group": input_data.blood_group,
            "units_required": input_data.units_required,
            "priority": input_data.priority,
            "hospital_id": None,
            "hospital_name": input_data.hospital_name,
            "patient_reason": input_data.patient_reason or "",
            "candidate_donors": [],
            "eligible_donors": [d.model_dump() for d in input_data.eligible_donors],
            "ranked_donors": [],
            "verified_hospital_ids": input_data.verified_hospital_ids,
            "is_urgent": input_data.priority.upper() in ["HIGH", "CRITICAL"],
            "notifications": []
        }
        res = generate_notifications_node(state)
        return {"notifications": res.get("notifications", [])}
    except Exception as e:
        logger.error(f"Error in generate-notifications endpoint: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail="Notification generation failed.")


if __name__ == "__main__":
    port = int(os.getenv("PORT", 8000))
    host = os.getenv("HOST", "127.0.0.1")
    uvicorn.run("app:app", host=host, port=port, reload=False)
