from fastapi.testclient import TestClient
from graph.workflow import workflow_app

from api.app import app

KEY = {"X-Internal-Key": "test-key"}
client = TestClient(app)


def test_legacy_workflow_does_not_directly_dispatch_recommendations():
    assert "send_notifications" not in workflow_app.get_graph().nodes

INVENTORIES = [
    {"facility_id": "h-1", "facility_name": "General", "blood_group": "O+", "current_units": 2, "minimum_threshold": 10, "expiring_soon_units": 0},
    {"facility_id": "h-2", "facility_name": "City", "blood_group": "O+", "current_units": 30, "minimum_threshold": 10, "expiring_soon_units": 4, "expiry_alert_days": 5},
    {"facility_id": "h-3", "facility_name": "Rural", "blood_group": "O+", "current_units": 12, "minimum_threshold": 10, "expiring_soon_units": 0},
]


def test_analyze_requires_the_internal_key():
    assert client.post("/analyze", json={"mode": "monitor"}).status_code == 401


def _monitor(inventories, requests=None):
    return client.post("/analyze", headers=KEY, json={"mode": "monitor", "inventories": inventories, "public_requests": requests or []}).json()


def test_low_hospital_and_every_holder_above_its_own_threshold_are_alerted_with_unit_counts():
    body = _monitor(INVENTORIES + [
        # At its threshold exactly: neither low nor a holder
        {"facility_id": "h-4", "facility_name": "Base", "blood_group": "O+", "current_units": 10, "minimum_threshold": 10},
        # Another group: never involved in an O+ shortage (exact group only)
        {"facility_id": "h-5", "facility_name": "Coastal", "blood_group": "O-", "current_units": 40, "minimum_threshold": 10},
    ])
    kinds = {(r["hospital_id"], r["kind"]) for r in body["recommendations"]}
    assert kinds == {("h-1", "InventoryShortage"), ("h-2", "InventoryShortageHelp"), ("h-3", "InventoryShortageHelp"),
                     ("h-2", "PacketsExpiringSoon")}
    shortage = next(r for r in body["recommendations"] if r["kind"] == "InventoryShortage")
    assert shortage["message"] == "You have only 2 units of O+ left. Hospitals holding O+: City (30 units), Rural (12 units). Consider a transfer request."
    assert shortage["dedupe_key"] == "InventoryShortage:O+"
    help_city = next(r for r in body["recommendations"] if r["kind"] == "InventoryShortageHelp" and r["hospital_id"] == "h-2")
    assert help_city["message"] == "General has only 2 units of O+ left. You hold 30 units of O+. Consider offering a transfer."
    assert help_city["dedupe_key"] == "InventoryShortageHelp:O+:h-1"
    # Unit counts only: no threshold figures anywhere
    assert all("threshold" not in r["message"] and "/10" not in r["message"] for r in body["recommendations"])


def test_no_holder_means_a_donor_request_hint_and_no_surplus_alerts():
    body = _monitor([
        {"facility_id": "h-1", "facility_name": "General", "blood_group": "A-", "current_units": 1, "minimum_threshold": 5},
        {"facility_id": "h-2", "facility_name": "City", "blood_group": "A-", "current_units": 3, "minimum_threshold": 5},
        {"facility_id": "h-3", "facility_name": "Rural", "blood_group": "B+", "current_units": 90, "minimum_threshold": 5},
    ])
    assert {r["kind"] for r in body["recommendations"]} == {"InventoryShortage"}  # nobody above threshold, no surplus alert
    assert all("consider a donor blood request" in r["message"] for r in body["recommendations"])


def test_expiring_packets_are_matched_to_the_exact_group_only():
    body = _monitor(INVENTORIES, [{"hospital_id": "h-9", "hospital_name": "Teaching", "blood_group": "AB+", "remaining_units": 2},
                                  {"hospital_id": "h-8", "hospital_name": "Northern", "blood_group": "O+", "remaining_units": 1}])
    expiring = next(r for r in body["recommendations"] if r["kind"] == "PacketsExpiringSoon")
    assert any(x.startswith("General") for x in expiring["related"])  # low on O+
    assert any("Northern" in x for x in expiring["related"])           # O+ request
    assert not any("Teaching" in x for x in expiring["related"])       # AB+ request: compatible but not the exact group


def test_emergency_ranks_hospitals_holding_stock():
    body = client.post("/analyze", headers=KEY, json={
        "mode": "emergency", "emergency": {"blood_group": "O-", "units_required": 5, "hospital_name": "General"},
        "stock_hospitals": [{"hospital_id": "a", "hospital_name": "A", "available_units": 2},
                            {"hospital_id": "b", "hospital_name": "B", "available_units": 6}]}).json()
    assert [r["hospital_id"] for r in body["recommendations"]] == ["b", "a"]
    assert body["recommendations"][0]["suggested_units"] == 5 and body["coverage"] == "covered"
