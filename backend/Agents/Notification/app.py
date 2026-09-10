import os
import uvicorn
import logging
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from dotenv import load_dotenv

from models import (
    ProcessBloodRequestInput,
    RankDonorsInput,
    GenerateNotificationsInput,
    AgentProcessResponse,
    RankedDonor,
    NotificationItem,
    NotificationAgentState
)
from graph import notification_agent_app
from nodes import rank_donors_node, generate_notifications_node

load_dotenv()

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(name)s: %(message)s")
logger = logging.getLogger("LifeLinkAgentAPI")

app = FastAPI(
    title="LifeLink Student 2 - Donor Discovery & Notification Agent",
    version="2.0.0",
    description="LangGraph + FastAPI + Google Gemini Agent for LifeLink Blood Management System."
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

@app.get("/health")
def health_check():
    api_key_configured = bool(os.getenv("GOOGLE_API_KEY") or os.getenv("GEMINI_API_KEY"))
    model_name = os.getenv("MODEL_NAME", "gemini-2.5-flash")
    return {
        "status": "Healthy",
        "agent": "LifeLink Donor Discovery & Notification Agent",
        "framework": "LangGraph + FastAPI",
        "model": model_name,
        "is_api_key_configured": api_key_configured
    }

@app.post("/process-request", response_model=AgentProcessResponse)
async def process_blood_request(input_data: ProcessBloodRequestInput):
    """
    Executes the full LangGraph Agent Workflow:
    CheckPriority -> FindEligibleDonors -> RankDonors -> GenerateNotifications
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
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/rank-donors")
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
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/generate-notifications")
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
        raise HTTPException(status_code=500, detail=str(e))

if __name__ == "__main__":
    port = int(os.getenv("PORT", 8000))
    host = os.getenv("HOST", "0.0.0.0")
    uvicorn.run("app:app", host=host, port=port, reload=True)
