from datetime import date, timedelta

from fastapi.testclient import TestClient

from app import app
from nodes import COMPATIBILITY_MATRIX, is_eligible_donor

KEY = {"X-Internal-Key": "test-key"}
client = TestClient(app)


def donor(user_id, group="O-", **extra):
    base = {"user_id": user_id, "full_name": f"Donor {user_id}", "blood_group": group, "account_status": "Active"}
    base.update(extra)
    return base


def test_agent_rechecks_every_eligibility_rule():
    compatible = COMPATIBILITY_MATRIX["O+"]
    today = date(2026, 9, 25)
    assert is_eligible_donor(donor("ok"), compatible, today)
    assert not is_eligible_donor(donor("incompatible", "A+"), compatible, today)
    assert not is_eligible_donor(donor("suspended", is_suspended=True), compatible, today)
    assert not is_eligible_donor(donor("blocked", is_blocked=True), compatible, today)
    assert not is_eligible_donor(donor("inactive", account_status="Suspended"), compatible, today)
    assert not is_eligible_donor(donor("recent", last_donation_date=(today - timedelta(days=100)).isoformat()), compatible, today)
    assert is_eligible_donor(donor("interval-ok", last_donation_date=(today - timedelta(days=120)).isoformat()), compatible, today)
    assert not is_eligible_donor(donor("too-old", date_of_birth="1960-01-01"), compatible, today)


def test_endpoints_require_the_internal_key():
    assert client.post("/process-request", json={"request_id": "r", "blood_group": "O+"}).status_code == 401
    assert client.post("/hospital-alerts", json={"alerts": []}).status_code == 401


def test_process_request_only_alerts_eligible_donors_and_urgent_hospitals():
    body = {
        "request_id": "r-1", "blood_group": "O+", "priority": "Critical", "hospital_name": "General Hospital",
        "available_donors": [donor("d-ok"), donor("d-suspended", is_suspended=True), donor("d-group", "B+"),
                             donor("d-recent", last_donation_date=date.today().isoformat())],
        "verified_hospital_ids": ["h-1"],
    }
    res = client.post("/process-request", headers=KEY, json=body).json()
    assert res["eligible_donors_count"] == 1
    donors = [n["recipient_id"] for n in res["notifications"] if n["recipient_type"] == "Donor"]
    hospitals = [n["recipient_id"] for n in res["notifications"] if n["recipient_type"] == "Hospital"]
    assert donors == ["d-ok"] and hospitals == ["h-1"]

    normal = client.post("/process-request", headers=KEY, json={**body, "priority": "Normal"}).json()
    assert all(n["recipient_type"] == "Donor" for n in normal["notifications"])


def test_hospital_alerts_are_composed_per_hospital():
    res = client.post("/hospital-alerts", headers=KEY, json={
        "alerts": [
            {"hospital_id": "h-2", "kind": "EmergencyStock", "blood_group": "O-", "units": 4},
            {"hospital_id": "h-3", "kind": "PacketsExpiringSoon", "blood_group": "A+", "units": 2, "related": ["City Hospital"]},
        ],
        "context": {"hospitalName": "General Hospital", "priority": "Critical", "unitsRequired": 3}}).json()
    first, second = res["notifications"]
    assert first["recipient_id"] == "h-2" and first["notification_type"] == "EmergencyStockAlert"
    assert "General Hospital" in first["message"] and "4 compatible unit" in first["message"]
    assert second["notification_type"] == "PacketsExpiringSoon" and "City Hospital" in second["message"]
