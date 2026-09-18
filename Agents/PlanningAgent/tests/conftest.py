import sys
import os
from pathlib import Path
import pytest
from unittest.mock import AsyncMock, patch

# Ensure Agents/PlanningAgent is on sys.path
PLANNING_AGENT_DIR = Path(__file__).parent.parent.resolve()
if str(PLANNING_AGENT_DIR) not in sys.path:
    sys.path.insert(0, str(PLANNING_AGENT_DIR))

from app import app
from fastapi.testclient import TestClient


@pytest.fixture
def client():
    """FastAPI synchronous test client."""
    return TestClient(app)


@pytest.fixture
def mock_agent1_success():
    """Mock successful response from Agent 1 (Screening)."""
    return {
        "success": True,
        "agent": "ScreeningAgent",
        "statusCode": 200,
        "data": {
            "session_id": "SESS-MOCK-12345",
            "acceptance_id": "ACC-TEST-001",
            "donor_user_id": "USER-001",
            "blood_request_id": "REQ-001",
            "status": "In Progress",
            "first_question": {
                "question_id": "GH_1",
                "text": "Are you feeling healthy and well today?",
                "response_type": "YesNo"
            },
            "total_estimated_questions": 15
        }
    }


@pytest.fixture
def mock_agent2_success():
    """Mock successful response from Agent 2 (Matching & Notifications)."""
    return {
        "success": True,
        "agent": "MatchingAgent",
        "statusCode": 200,
        "data": {
            "request_id": "REQ-TEST-001",
            "is_urgent": True,
            "ranked_donors": [
                {
                    "donor_id": "DONOR-001",
                    "full_name": "Alice Johnson",
                    "blood_group": "O+",
                    "compatibility_score": 98.5,
                    "distance_km": 4.2
                },
                {
                    "donor_id": "DONOR-002",
                    "full_name": "Bob Smith",
                    "blood_group": "O+",
                    "compatibility_score": 94.0,
                    "distance_km": 6.8
                }
            ],
            "notifications": [
                {
                    "recipient_id": "DONOR-001",
                    "recipient_role": "Donor",
                    "title": "Urgent Blood Donation Needed",
                    "message": "A patient near you requires 2 units of O+ blood."
                }
            ],
            "summary": "2 compatible donors found and ranked."
        }
    }


@pytest.fixture
def mock_agent3_success():
    """Mock successful response from Agent 3 (Inventory Management)."""
    return {
        "success": True,
        "agent": "InventoryAgent",
        "statusCode": 200,
        "data": {
            "shortages": [
                {
                    "hospital_id": "HOSP-001",
                    "blood_group": "B-",
                    "units_needed": 5,
                    "urgency": "CRITICAL"
                }
            ],
            "surpluses": [
                {
                    "hospital_id": "HOSP-002",
                    "blood_group": "B-",
                    "surplus_units": 10
                }
            ],
            "recommendations": [
                {
                    "from_hospital_id": "HOSP-002",
                    "to_hospital_id": "HOSP-001",
                    "blood_group": "B-",
                    "recommended_units": 5,
                    "reason": "Immediate balance of critical shortage from nearby surplus stock."
                }
            ]
        }
    }
