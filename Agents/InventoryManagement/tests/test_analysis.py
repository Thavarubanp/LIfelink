from fastapi.testclient import TestClient

from api.app import app

KEY = {"X-Internal-Key": "test-key"}
client = TestClient(app)

INVENTORIES = [
    {"facility_id": "h-1", "facility_name": "General", "blood_group": "O+", "current_units": 2, "minimum_threshold": 10, "expiring_soon_units": 0},
    {"facility_id": "h-2", "facility_name": "City", "blood_group": "O+", "current_units": 30, "minimum_threshold": 10, "expiring_soon_units": 4, "expiry_alert_days": 5},
    {"facility_id": "h-3", "facility_name": "Rural", "blood_group": "O+", "current_units": 12, "minimum_threshold": 10, "expiring_soon_units": 0},
]


def test_analyze_requires_the_internal_key():
    assert client.post("/analyze", json={"mode": "monitor"}).status_code == 401


def test_monitor_finds_shortages_with_sources_and_expiring_stock_with_takers():
    body = client.post("/analyze", headers=KEY, json={
        "mode": "monitor", "inventories": INVENTORIES,
        "public_requests": [{"hospital_id": "h-9", "hospital_name": "Teaching", "blood_group": "AB+", "remaining_units": 2}]}).json()
    kinds = {(r["hospital_id"], r["kind"]) for r in body["recommendations"]}
    assert kinds == {("h-1", "InventoryShortage"), ("h-2", "PacketsExpiringSoon")}
    shortage = next(r for r in body["recommendations"] if r["kind"] == "InventoryShortage")
    assert shortage["units"] == 8 and shortage["related"][0].startswith("City")
    expiring = next(r for r in body["recommendations"] if r["kind"] == "PacketsExpiringSoon")
    assert "General" in expiring["related"] and any("Teaching" in x for x in expiring["related"])  # O+ can be given to AB+


def test_emergency_ranks_hospitals_holding_stock():
    body = client.post("/analyze", headers=KEY, json={
        "mode": "emergency", "emergency": {"blood_group": "O-", "units_required": 5, "hospital_name": "General"},
        "stock_hospitals": [{"hospital_id": "a", "hospital_name": "A", "available_units": 2},
                            {"hospital_id": "b", "hospital_name": "B", "available_units": 6}]}).json()
    assert [r["hospital_id"] for r in body["recommendations"]] == ["b", "a"]
    assert body["recommendations"][0]["suggested_units"] == 5 and body["coverage"] == "covered"
