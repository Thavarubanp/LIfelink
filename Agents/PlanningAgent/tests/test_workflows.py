import pytest
from unittest.mock import AsyncMock, patch

from models.request import PlanRequest
from services.planner_service import planner_service


@pytest.mark.asyncio
async def test_workflow_1_blood_request_approved(mock_agent2_success):
    """
    Workflow 1: BloodRequestApproved
    Verifies that approving a blood request routes to Agent 2 (Matching & Notifications)
    and aggregates ranked donors into an actionable plan.
    """
    with patch("services.agent_clients.agent_clients.call_matching_agent", new_callable=AsyncMock) as mock_agent2:
        mock_agent2.return_value = mock_agent2_success

        request = PlanRequest(
            eventType="BloodRequestApproved",
            payload={
                "requestId": "REQ-TEST-001",
                "bloodGroup": "O+",
                "unitsRequired": 2,
                "priority": "URGENT",
                "hospitalId": "HOSP-001",
                "hospitalName": "City General Hospital"
            }
        )

        response = await planner_service.generate_plan(request)

        # Assertions
        assert response.success is True
        assert response.workflow == "BloodRequestApproved"
        assert response.agentsInvoked == ["MatchingAgent"]
        assert response.executionPlan.workflowStatus == "COMPLETED"
        assert "Agent 2 ranked 2 compatible donors" in response.executionPlan.summary

        # Action item assertions
        actions = response.executionPlan.actionItems
        assert len(actions) == 1
        assert actions[0].agent == "MatchingAgent"
        assert actions[0].action == "DISPATCH_DONOR_NOTIFICATIONS"
        assert actions[0].status == "SUCCESS"
        assert actions[0].details["rankedDonorsCount"] == 2
        assert len(actions[0].details["topDonors"]) == 2

        mock_agent2.assert_awaited_once()


@pytest.mark.asyncio
async def test_workflow_2_donor_accepted(mock_agent1_success):
    """
    Workflow 2: DonorAccepted
    Verifies that a donor accepting a request triggers Agent 1 (Screening)
    to start a medical screening session.
    """
    with patch("services.agent_clients.agent_clients.call_screening_agent", new_callable=AsyncMock) as mock_agent1:
        mock_agent1.return_value = mock_agent1_success

        request = PlanRequest(
            eventType="DonorAccepted",
            payload={
                "acceptanceId": "ACC-TEST-001",
                "donorUserId": "USER-001",
                "bloodRequestId": "REQ-001"
            }
        )

        response = await planner_service.generate_plan(request)

        # Assertions
        assert response.success is True
        assert response.workflow == "DonorAccepted"
        assert response.agentsInvoked == ["ScreeningAgent"]
        assert response.executionPlan.workflowStatus == "COMPLETED"
        assert "SESS-MOCK-12345" in response.executionPlan.summary

        actions = response.executionPlan.actionItems
        assert len(actions) == 1
        assert actions[0].agent == "ScreeningAgent"
        assert actions[0].action == "LAUNCH_DONOR_SCREENING_SESSION"
        assert actions[0].status == "SUCCESS"
        assert actions[0].details["sessionId"] == "SESS-MOCK-12345"
        assert actions[0].details["firstQuestion"]["question_id"] == "GH_1"

        mock_agent1.assert_awaited_once_with("ACC-TEST-001", request.payload)


@pytest.mark.asyncio
async def test_workflow_3_emergency_shortage(mock_agent2_success, mock_agent3_success):
    """
    Workflow 3: EmergencyShortage
    Verifies sequential multi-agent execution:
    1. Agent 3 (Inventory) identifies surplus transfers
    2. Agent 2 (Matching) locates emergency donors
    Aggregates both into a unified dual-pronged mitigation plan.
    """
    with patch("services.agent_clients.agent_clients.call_inventory_agent", new_callable=AsyncMock) as mock_agent3, \
         patch("services.agent_clients.agent_clients.call_matching_agent", new_callable=AsyncMock) as mock_agent2:

        mock_agent3.return_value = mock_agent3_success
        mock_agent2.return_value = mock_agent2_success

        request = PlanRequest(
            eventType="EmergencyShortage",
            payload={
                "hospitalId": "HOSP-001",
                "bloodGroup": "B-",
                "unitsRequired": 5,
                "priority": "CRITICAL"
            }
        )

        response = await planner_service.generate_plan(request)

        # Assertions
        assert response.success is True
        assert response.workflow == "EmergencyShortage"
        assert response.agentsInvoked == ["InventoryAgent", "MatchingAgent"]
        assert response.executionPlan.workflowStatus == "COMPLETED"
        assert "1 inter-hospital transfer recommendations" in response.executionPlan.summary

        # Verifies both action directives generated
        actions = response.executionPlan.actionItems
        assert len(actions) == 2

        inv_action = next(a for a in actions if a.agent == "InventoryAgent")
        assert inv_action.action == "INITIATE_INTER_HOSPITAL_TRANSFERS"
        assert inv_action.status == "SUCCESS"
        assert inv_action.details["recommendationsCount"] == 1

        match_action = next(a for a in actions if a.agent == "MatchingAgent")
        assert match_action.action == "BROADCAST_EMERGENCY_DONOR_ALERTS"
        assert match_action.status == "SUCCESS"
        assert match_action.details["matchedDonorsCount"] == 2

        mock_agent3.assert_awaited_once()
        mock_agent2.assert_awaited_once()


@pytest.mark.asyncio
async def test_unknown_event_type_handling():
    """
    Verifies that unknown or unsupported event types return clean FAILED responses
    without crashing the microservice.
    """
    request = PlanRequest(
        eventType="UnrecognizedEvent123",
        payload={"someKey": "someValue"}
    )

    response = await planner_service.generate_plan(request)

    assert response.success is False
    assert response.workflow == "UnknownWorkflow"
    assert response.executionPlan.workflowStatus == "FAILED"
    assert "Unsupported event type" in response.executionPlan.summary


@pytest.mark.asyncio
async def test_downstream_agent_failure_graceful_degradation():
    """
    Verifies that if a downstream microservice fails (e.g. timeout or 503),
    the Planning Agent captures the failure gracefully and returns actionable diagnostics.
    """
    failure_response = {
        "success": False,
        "agent": "MatchingAgent",
        "error": "Agent 2 connection failure (ConnectError): Connection refused",
        "statusCode": 503
    }

    with patch("services.agent_clients.agent_clients.call_matching_agent", new_callable=AsyncMock) as mock_agent2:
        mock_agent2.return_value = failure_response

        request = PlanRequest(
            eventType="BloodRequestApproved",
            payload={"requestId": "REQ-FAIL-001"}
        )

        response = await planner_service.generate_plan(request)

        assert response.success is False
        assert response.executionPlan.workflowStatus == "FAILED"
        assert "Agent 2 (Matching) failed" in response.executionPlan.summary

        actions = response.executionPlan.actionItems
        assert len(actions) == 1
        assert actions[0].status == "FAILED"
        assert "Connection refused" in actions[0].details["error"]
