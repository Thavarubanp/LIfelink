"""
Inventory analysis for the Supervisor (no side effects: the backend saves any resulting alerts).

monitor   - the owner's rule (exact blood group only, no compatible groups, no surplus alerts):
            * a group is LOW when current < threshold. The low hospital gets an "InventoryShortage" alert listing the
              other hospitals that hold that exact group ABOVE their own threshold;
            * every one of those hospitals (current > its own threshold) gets an "InventoryShortageHelp" alert;
              hospitals at or below their threshold are not asked to help;
            * packets expiring inside a hospital's alert window are matched to hospitals low on the exact group and to
              open public requests for the exact group.
            Alert text shows unit counts only, never threshold figures. Each alert carries a stable dedupe_key.
emergency - hospitals holding stock for an emergency (the backend sends only exact-group holders), ranked by units.
"""
from typing import Any, Dict, List


def _int(value: Any) -> int:
    try:
        return int(value or 0)
    except (TypeError, ValueError):
        return 0


def _units(n: int) -> str:
    return "1 unit" if n == 1 else f"{n} units"


def is_below_threshold(current: int, threshold: int) -> bool:
    """The single "below threshold" rule, the same as the backend's InventoryRules."""
    return current < threshold


def is_above_threshold(current: int, threshold: int) -> bool:
    return current > threshold


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
        group = row["blood_group"]
        if is_below_threshold(row["current"], row["threshold"]):
            holders = sorted((o for o in rows if o["hospital_id"] != row["hospital_id"] and o["blood_group"] == group
                              and is_above_threshold(o["current"], o["threshold"])), key=lambda o: o["current"], reverse=True)
            shortages.append({**row, "deficit": row["threshold"] - row["current"]})
            listed = ", ".join(f"{o['hospital_name']} ({_units(o['current'])})" for o in holders[:5])
            recommendations.append({
                "hospital_id": row["hospital_id"], "hospital_name": row["hospital_name"], "kind": "InventoryShortage",
                "blood_group": group, "units": row["current"], "urgency": (row["threshold"] - row["current"]) / max(row["threshold"], 1),
                "related": [f"{o['hospital_name']} ({_units(o['current'])})" for o in holders[:5]],
                "dedupe_key": f"InventoryShortage:{group}",
                "message": f"You have only {_units(row['current'])} of {group} left."
                           + (f" Hospitals holding {group}: {listed}. Consider a transfer request." if holders
                              else f" No other hospital currently holds extra {group}; consider a donor blood request."),
            })
            for holder in holders:
                recommendations.append({
                    "hospital_id": holder["hospital_id"], "hospital_name": holder["hospital_name"], "kind": "InventoryShortageHelp",
                    "blood_group": group, "units": holder["current"], "urgency": (row["threshold"] - row["current"]) / max(row["threshold"], 1),
                    "related": [row["hospital_name"]],
                    "dedupe_key": f"InventoryShortageHelp:{group}:{row['hospital_id']}",
                    "message": f"{row['hospital_name']} has only {_units(row['current'])} of {group} left. "
                               f"You hold {_units(holder['current'])} of {group}. Consider offering a transfer.",
                })

        if row["expiring"] > 0:
            takers = [f"{o['hospital_name']} ({_units(o['current'])} left)" for o in rows if o["hospital_id"] != row["hospital_id"]
                      and o["blood_group"] == group and is_below_threshold(o["current"], o["threshold"])][:3]
            # Exact group only (owner's Q3): an expiring A+ packet is offered to A+ requests, never to compatible groups
            requests = [f"{r.get('hospital_name', 'a hospital')} request for {group} ({_units(_int(r.get('remaining_units')))} needed)"
                        for r in public_requests
                        if str(r.get("blood_group")) == group and str(r.get("hospital_id")) != row["hospital_id"]][:3]
            expiring.append(row)
            related = takers + requests
            recommendations.append({
                "hospital_id": row["hospital_id"], "hospital_name": row["hospital_name"], "kind": "PacketsExpiringSoon",
                "blood_group": group, "units": row["expiring"], "urgency": row["expiring"],
                "related": related,
                "dedupe_key": f"PacketsExpiringSoon:{group}",
                "message": f"{row['expiring']} {group} packet(s) expire within {row['alert_days']} day(s)."
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
