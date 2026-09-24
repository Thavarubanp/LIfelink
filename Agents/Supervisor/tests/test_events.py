from unittest.mock import AsyncMock, patch

from tests.conftest import KEY, down, ok

DONOR_ALERTS = ok({"notifications": [
    {"recipient_type": "Donor", "recipient_id": "d-1", "title": "Blood needed", "message": "Please help"},
    {"recipient_type": "Hospital", "recipient_id": "h-1", "title": "Urgent", "message": "Shortage"},
]})


def test_plan_requires_internal_key(client):
    assert client.post("/plan", json={"eventType": "DonorAccepted"}).status_code == 401
    assert client.post("/plan", json={"eventType": "DonorAccepted"}, headers={"X-Internal-Key": "wrong"}).status_code == 401


def test_blood_request_approved_returns_notifications_for_backend(client):
    with patch("services.agent_clients.agent_clients.donor_alerts", new=AsyncMock(return_value=DONOR_ALERTS)) as alerts:
        res = client.post("/plan", headers=KEY, json={"eventType": "BloodRequestApproved", "requestId": "r-1", "bloodGroup": "O+",
                                                      "urgency": "Critical", "payload": {"availableDonors": [{"user_id": "d-1"}],
                                                                                         "verifiedHospitalIds": ["h-1"]}})
    body = res.json()
    assert res.status_code == 200 and body["success"] is True
    sent = alerts.call_args.args[0]
    assert sent["available_donors"] == [{"user_id": "d-1"}] and sent["priority"] == "Critical"
    types = {n["recipientId"]: n["notificationType"] for n in body["notifications"]}
    assert types == {"d-1": "EligibleDonorAlert", "h-1": "UrgentHospitalAlert"}
    assert body["agentsInvoked"] == ["Planning", "NotificationAgent"]


def test_notification_agent_down_reports_failure_so_backend_falls_back(client):
    with patch("services.agent_clients.agent_clients.donor_alerts", new=AsyncMock(return_value=down("NotificationAgent"))):
        body = client.post("/plan", headers=KEY, json={"eventType": "BloodRequestApproved", "payload": {}}).json()
    assert body["success"] is False and body["workflowStatus"] == "FAILED" and body["notifications"] == []


def test_donor_accepted_opens_screening_session(client):
    with patch("services.agent_clients.agent_clients.screening_start", new=AsyncMock(return_value=ok({"session_id": "s-1"}))) as start:
        body = client.post("/plan", headers=KEY, json={"eventType": "DonorAccepted", "payload": {"acceptanceId": "a-1"}}).json()
    start.assert_awaited_once_with("a-1")
    assert body["success"] is True


def test_donor_accepted_without_acceptance_fails(client):
    body = client.post("/plan", headers=KEY, json={"eventType": "DonorAccepted", "payload": {}}).json()
    assert body["success"] is False


def test_emergency_shortage_runs_inventory_then_notification(client):
    recs = ok({"recommendations": [{"hospital_id": "h-2", "kind": "EmergencyStock", "blood_group": "O-", "units": 3}]})
    alerts = ok({"notifications": [{"recipient_id": "h-2", "notification_type": "EmergencyStockAlert", "title": "Emergency", "message": "Help"}]})
    with patch("services.agent_clients.agent_clients.inventory_analyze", new=AsyncMock(return_value=recs)) as analyze, \
         patch("services.agent_clients.agent_clients.hospital_alerts", new=AsyncMock(return_value=alerts)) as hospital:
        body = client.post("/plan", headers=KEY, json={"eventType": "EmergencyShortage", "bloodGroup": "O-", "unitsRequired": 3,
                                                       "payload": {"stockHospitals": [{"hospital_id": "h-2", "available_units": 4}]}}).json()
    assert analyze.call_args.args[0]["mode"] == "emergency"
    assert hospital.call_args.args[0][0]["hospital_id"] == "h-2"
    assert body["success"] is True
    assert body["notifications"][0]["notificationType"] == "EmergencyStockAlert"
    assert body["agentsInvoked"] == ["Planning", "InventoryAgent", "NotificationAgent"]


def test_inventory_check_keeps_the_most_important_alerts_per_hospital(client):
    recs = [{"hospital_id": "h-1", "kind": k, "blood_group": g} for k, g in
            [("PacketsExpiringSoon", "A+"), ("InventoryShortage", "O-"), ("TransferSuggestion", "B+"),
             ("InventoryShortage", "O+"), ("PacketsExpiringSoon", "A+")]]
    with patch("services.agent_clients.agent_clients.inventory_analyze", new=AsyncMock(return_value=ok({"recommendations": recs}))), \
         patch("services.agent_clients.agent_clients.hospital_alerts", new=AsyncMock(return_value=ok({"notifications": []}))) as hospital:
        body = client.post("/plan", headers=KEY, json={"eventType": "InventoryCheck", "payload": {"inventories": []}}).json()
    kept = hospital.call_args.args[0]
    assert len(kept) == 3  # duplicate dropped, at most 3 per hospital
    assert [r["kind"] for r in kept[:2]] == ["InventoryShortage", "InventoryShortage"]
    assert body["success"] is True


def test_unknown_event_fails(client):
    body = client.post("/plan", headers=KEY, json={"eventType": "Nope"}).json()
    assert body["success"] is False
