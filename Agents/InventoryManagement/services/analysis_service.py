"""
Inventory analysis for the Supervisor (no side effects: the backend saves any resulting alerts).

monitor   - shortages (below threshold) with hospitals that have spare stock, and packets expiring inside each
            hospital's alert window matched to hospitals below threshold and open public requests that could use them.
emergency - hospitals holding compatible, unexpired stock for an emergency, ranked by how much they can give.
"""
from typing import Any, Dict, List

# Recipient blood group -> red cell compatible donor groups
COMPATIBLE_DONORS: Dict[str, List[str]] = {
    "O-": ["O-"], "O+": ["O+", "O-"], "A-": ["A-", "O-"], "A+": ["A+", "A-", "O+", "O-"],
    "B-": ["B-", "O-"], "B+": ["B+", "B-", "O+", "O-"], "AB-": ["AB-", "A-", "B-", "O-"],
    "AB+": ["AB+", "AB-", "A+", "A-", "B+", "B-", "O+", "O-"],
}


def _int(value: Any) -> int:
    try:
        return int(value or 0)
    except (TypeError, ValueError):
        return 0


def monitor(inventories: List[Dict[str, Any]], public_requests: List[Dict[str, Any]]) -> Dict[str, Any]:
    rows = [{
        "hospital_id": str(r.get("facility_id") or r.get("hospital_id")),
        "hospital_name": r.get("facility_name") or r.get("hospital_name") or "Hospital",
        "blood_group": r.get("blood_group"),
        "current": _int(r.get("current_units")),
        "threshold": _int(r.get("minimum_threshold")),
        "expiring": _int(r.get("expiring_soon_units")),
        "alert_days": _int(r.get("expiry_alert_days")) or 5,
    } for r in inventories if r.get("blood_group")]

    shortages, expiring, recommendations = [], [], []
    for row in rows:
        if row["current"] < row["threshold"]:
            deficit = row["threshold"] - row["current"]
            sources = sorted((o for o in rows if o["hospital_id"] != row["hospital_id"] and o["blood_group"] == row["blood_group"]
                              and o["current"] > o["threshold"]), key=lambda o: o["current"] - o["threshold"], reverse=True)[:3]
            shortages.append({**row, "deficit": deficit})
            recommendations.append({
                "hospital_id": row["hospital_id"], "hospital_name": row["hospital_name"], "kind": "InventoryShortage",
                "blood_group": row["blood_group"], "units": deficit, "urgency": deficit / max(row["threshold"], 1),
                "related": [f"{o['hospital_name']} ({o['current'] - o['threshold']} spare)" for o in sources],
                "message": f"{row['blood_group']} stock is {row['current']} unit(s), below your threshold of {row['threshold']}."
                           + (f" Hospitals with spare {row['blood_group']}: " + ", ".join(f"{o['hospital_name']} ({o['current'] - o['threshold']} spare)" for o in sources)
                              + ". Consider a transfer request." if sources else " No hospital currently has spare stock; consider a donor request."),
            })

        if row["expiring"] > 0:
            takers = [o["hospital_name"] for o in rows if o["hospital_id"] != row["hospital_id"]
                      and o["blood_group"] == row["blood_group"] and o["current"] < o["threshold"]][:3]
            requests = [f"{r.get('hospital_name', 'a hospital')} request for {r.get('blood_group')} ({_int(r.get('remaining_units'))} unit(s) needed)"
                        for r in public_requests
                        if row["blood_group"] in COMPATIBLE_DONORS.get(str(r.get("blood_group")), [])
                        and str(r.get("hospital_id")) != row["hospital_id"]][:3]
            expiring.append(row)
            related = takers + requests
            recommendations.append({
                "hospital_id": row["hospital_id"], "hospital_name": row["hospital_name"], "kind": "PacketsExpiringSoon",
                "blood_group": row["blood_group"], "units": row["expiring"], "urgency": row["expiring"],
                "related": related,
                "message": f"{row['expiring']} {row['blood_group']} packet(s) expire within {row['alert_days']} day(s)."
                           + (" Could be used by: " + "; ".join(related) + ". Consider offering them before they expire." if related
                              else " Use them first to prevent wastage."),
            })

    return {"shortages": shortages, "expiring": expiring, "recommendations": recommendations}


def emergency(request: Dict[str, Any], stock_hospitals: List[Dict[str, Any]]) -> Dict[str, Any]:
    needed = _int(request.get("units_required")) or 1
    ranked = sorted(stock_hospitals, key=lambda h: _int(h.get("available_units")), reverse=True)
    recommendations = [{
        "hospital_id": str(h.get("hospital_id")), "hospital_name": h.get("hospital_name"), "kind": "EmergencyStock",
        "blood_group": request.get("blood_group"), "units": _int(h.get("available_units")),
        "suggested_units": min(_int(h.get("available_units")), needed), "urgency": 10,
        "related": [str(request.get("hospital_name") or "")],
    } for h in ranked if _int(h.get("available_units")) > 0]
    covered = sum(_int(h.get("available_units")) for h in ranked)
    return {"recommendations": recommendations, "coverage": "covered" if covered >= needed else "partial" if covered else "none"}
