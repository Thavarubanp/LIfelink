import pytest
from unittest.mock import AsyncMock, patch


def test_root_endpoint(client):
    """Verifies service metadata at root endpoint."""
    response = client.get("/")
    assert response.status_code == 200
    data = response.json()
    assert "LifeLink Planning Agent" in data["service"]
    assert data["docs"] == "/docs"
    assert data["health"] == "/health"
    assert data["planEndpoint"] == "/plan"


def test_health_endpoint(client):
    """Verifies health check and downstream diagnostics."""
    mock_health = {
        "Agent1_Screening": {"url": "http://localhost:8001/api/agent/health", "status": "REACHABLE", "latencyMs": 5.2, "error": None},
        "Agent2_Matching": {"url": "http://localhost:8000/health", "status": "REACHABLE", "latencyMs": 4.1, "error": None},
        "Agent3_Inventory": {"url": "http://localhost:8003/health", "status": "REACHABLE", "latencyMs": 6.8, "error": None},
    }

    with patch("services.agent_clients.agent_clients.check_health", new_callable=AsyncMock) as mock_probe:
        mock_probe.return_value = mock_health

        response = client.get("/health")
        assert response.status_code == 200
        data = response.json()
        assert data["status"] == "Healthy"
        assert data["service"] == "LifeLink Planning Agent (Workflow Orchestrator)"
        assert len(data["downstreamAgents"]) == 3
        assert data["downstreamAgents"]["Agent1_Screening"]["status"] == "REACHABLE"


def test_post_plan_valid(client, mock_agent2_success):
    """Verifies POST /plan execution for BloodRequestApproved."""
    with patch("services.agent_clients.agent_clients.call_matching_agent", new_callable=AsyncMock) as mock_agent2:
        mock_agent2.return_value = mock_agent2_success

        payload = {
            "eventType": "BloodRequestApproved",
            "payload": {
                "requestId": "REQ-API-100",
                "bloodGroup": "A+",
                "unitsRequired": 1
            }
        }

        response = client.post("/plan", json=payload)
        assert response.status_code == 200
        data = response.json()
        assert data["success"] is True
        assert data["workflow"] == "BloodRequestApproved"
        assert "MatchingAgent" in data["agentsInvoked"]
        assert data["executionPlan"]["workflowStatus"] == "COMPLETED"
        assert len(data["executionPlan"]["actionItems"]) == 1


def test_post_plan_donor_accepted(client, mock_agent1_success):
    """Verifies POST /plan execution for DonorAccepted."""
    with patch("services.agent_clients.agent_clients.call_screening_agent", new_callable=AsyncMock) as mock_agent1:
        mock_agent1.return_value = mock_agent1_success

        payload = {
            "eventType": "DonorAccepted",
            "payload": {
                "acceptanceId": "ACC-API-200",
                "donorUserId": "USER-API-001",
                "bloodRequestId": "REQ-API-100"
            }
        }

        response = client.post("/plan", json=payload)
        assert response.status_code == 200
        data = response.json()
        assert data["success"] is True
        assert data["workflow"] == "DonorAccepted"
        assert "ScreeningAgent" in data["agentsInvoked"]
        assert data["executionPlan"]["workflowStatus"] == "COMPLETED"
        assert len(data["executionPlan"]["actionItems"]) == 1
        assert data["executionPlan"]["actionItems"][0]["agent"] == "ScreeningAgent"


def test_post_plan_emergency_shortage(client, mock_agent2_success, mock_agent3_success):
    """Verifies POST /plan execution for EmergencyShortage."""
    with patch("services.agent_clients.agent_clients.call_inventory_agent", new_callable=AsyncMock) as mock_agent3, \
         patch("services.agent_clients.agent_clients.call_matching_agent", new_callable=AsyncMock) as mock_agent2:
        mock_agent3.return_value = mock_agent3_success
        mock_agent2.return_value = mock_agent2_success

        payload = {
            "eventType": "EmergencyShortage",
            "payload": {
                "hospitalId": "HOSP-API-001",
                "bloodGroup": "O-",
                "unitsRequired": 4,
                "priority": "CRITICAL"
            }
        }

        response = client.post("/plan", json=payload)
        assert response.status_code == 200
        data = response.json()
        assert data["success"] is True
        assert data["workflow"] == "EmergencyShortage"
        assert "InventoryAgent" in data["agentsInvoked"]
        assert "MatchingAgent" in data["agentsInvoked"]
        assert data["executionPlan"]["workflowStatus"] == "COMPLETED"
        assert len(data["executionPlan"]["actionItems"]) == 2


def test_post_plan_missing_event_type(client):
    """Verifies 422 Unprocessable Entity when eventType is omitted."""
    response = client.post("/plan", json={"payload": {}})
    assert response.status_code == 422
