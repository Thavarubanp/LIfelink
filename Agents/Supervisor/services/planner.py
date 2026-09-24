"""
Planning capability of the Supervisor: prioritisation and next-step suggestions.

Everything here is deterministic. Account summaries use only the role-scoped snapshot the backend sent for the
signed-in user, so the assistant can never describe data the user is not allowed to see.
"""
from datetime import datetime, timezone
from typing import Any, Dict, List, Tuple

PRIORITY_WEIGHT = {"CRITICAL": 3, "HIGH": 2, "NORMAL": 1, "MEDIUM": 1, "LOW": 0}
ALERT_WEIGHT = {"EmergencyStock": 4, "InventoryShortage": 3, "PacketsExpiringSoon": 2, "TransferSuggestion": 1}
MAX_ALERTS_PER_HOSPITAL = 3


def priority_score(priority: str, units: int = 1) -> int:
    return PRIORITY_WEIGHT.get((priority or "Normal").upper(), 1) * 10 + min(int(units or 1), 10)


def triage_request(payload: Dict[str, Any]) -> Dict[str, Any]:
    priority = str(payload.get("priority") or payload.get("urgency") or "Normal")
    units = int(payload.get("unitsRequired") or payload.get("units_required") or 1)
    donors = len(payload.get("availableDonors") or [])
    score = priority_score(priority, units)
    return {
        "priority": priority,
        "score": score,
        "alertHospitals": priority.upper() in ("HIGH", "CRITICAL"),
        "summary": f"{priority} request for {units} unit(s); {donors} eligible donor(s) selected by the backend.",
    }


def triage_emergency(payload: Dict[str, Any]) -> Dict[str, Any]:
    units = int(payload.get("unitsRequired") or 1)
    holders = payload.get("stockHospitals") or []
    available = sum(int(h.get("available_units") or 0) for h in holders)
    return {
        "priority": str(payload.get("priority") or "High"),
        "coverage": "covered" if available >= units else "partial" if available else "none",
        "summary": f"Emergency needs {units} unit(s); {len(holders)} hospital(s) hold {available} compatible unit(s).",
    }


def prioritise_alerts(recommendations: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    """Keeps the most important alerts per hospital and drops duplicates (same hospital, kind and blood group)."""
    seen = set()
    per_hospital: Dict[str, int] = {}
    ordered = sorted(recommendations, key=lambda r: (ALERT_WEIGHT.get(r.get("kind", ""), 0), r.get("urgency", 0)), reverse=True)
    kept = []
    for rec in ordered:
        key = (rec.get("hospital_id"), rec.get("kind"), rec.get("blood_group"))
        if key in seen:
            continue
        hospital = str(rec.get("hospital_id"))
        if per_hospital.get(hospital, 0) >= MAX_ALERTS_PER_HOSPITAL:
            continue
        seen.add(key)
        per_hospital[hospital] = per_hospital.get(hospital, 0) + 1
        kept.append(rec)
    return kept


def _fmt_date(value: Any) -> str:
    if not value:
        return ""
    try:
        return datetime.fromisoformat(str(value).replace("Z", "+00:00")).strftime("%d %b %Y")
    except ValueError:
        return str(value)


def account_summary(role: str, snapshot: Dict[str, Any]) -> Tuple[str, List[Dict[str, str]]]:
    """Returns (text, suggested actions) for 'what is my status / what should I do next' questions."""
    role = (snapshot.get("role") or role or "Donor")
    if role == "Doctor":
        return _doctor_summary(snapshot)
    if role == "HospitalStaff":
        return _hospital_summary(snapshot)
    if role == "Admin":
        return _admin_summary(snapshot)
    return _donor_summary(snapshot)


def _donor_summary(s: Dict[str, Any]) -> Tuple[str, List[Dict[str, str]]]:
    lines: List[str] = []
    actions: List[Dict[str, str]] = []
    profile = s.get("profile") or {}
    group = profile.get("bloodGroup")
    lines.append(f"Your blood group on record: {group}{' (confirmed)' if profile.get('bloodGroupConfirmed') else ''}." if group
                 else "You have not set your blood group yet; add it in your profile or when you accept a request.")
    if profile.get("canDonateNow") is False and profile.get("nextEligibleDate"):
        lines.append(f"You can donate again from {_fmt_date(profile['nextEligibleDate'])} (120 days after your last donation).")
    elif profile.get("canDonateNow"):
        lines.append("You are within the donation interval rules and can accept a request if you feel well.")

    next_steps = {
        "Accepted": "start your screening interview",
        "ScreeningPending": "continue your screening interview",
        "ScreeningCompleted": "wait for the doctor to review your screening report",
        "Verified": "visit the hospital to donate; a slot is reserved for you",
    }
    for a in (s.get("acceptances") or [])[:3]:
        status = a.get("status")
        where = f"{a.get('requestBloodGroup', '')} request at {a.get('hospitalName', 'the hospital')}".strip()
        report = a.get("latestReport") or {}
        if status in next_steps:
            lines.append(f"Your donation for the {where}: {next_steps[status]}.")
            if status in ("Accepted", "ScreeningPending") and a.get("acceptanceId"):
                actions.append({"label": "Continue screening", "route": f"/donor/acceptances/{a['acceptanceId']}/screening"})
            if status == "Verified" and report.get("approvalNotes"):
                lines.append(f"Doctor's notes: {report['approvalNotes']}")
        elif status == "Rejected":
            lines.append(f"Your screening for the {where} was not approved. Reason: {a.get('rejectionReason') or report.get('rejectionReason') or 'not given'}.")
        elif status == "Matched":
            lines.append(f"Thank you: your donation for the {where} was recorded.")
    if s.get("acceptances"):
        actions.append({"label": "My Acceptances", "route": "/donor/acceptances"})

    for r in (s.get("requests") or [])[:3]:
        text = f"Your {r.get('bloodGroup')} request at {r.get('hospitalName', 'the hospital')} is {r.get('status')}"
        if r.get("status") in ("Approved", "Completed"):
            text += f" ({r.get('fulfilledUnits', 0)} of {r.get('unitsRequired')} donated, {r.get('reservedUnits', 0)} reserved)"
        if r.get("status") == "Rejected" and r.get("rejectionReason"):
            text += f"; reason: {r['rejectionReason']}"
        lines.append(text + ".")
    if s.get("requests"):
        actions.append({"label": "My requests", "route": "/donor/requests/create"})

    if not s.get("acceptances") and not s.get("requests"):
        lines.append("You have no active donations or requests.")
        actions.append({"label": "Available requests", "route": "/donor/requests"})
    unread = (s.get("notifications") or {}).get("unread")
    if unread:
        lines.append(f"You have {unread} unread notification(s).")
    return " ".join(lines), actions


def _doctor_summary(s: Dict[str, Any]) -> Tuple[str, List[Dict[str, str]]]:
    lines: List[str] = []
    reports = s.get("pendingReports") or {}
    total, mine = int(reports.get("total") or 0), int(reports.get("assignedToMe") or 0)
    if total:
        high = sum(1 for r in reports.get("items") or [] if str(r.get("riskLevel", "")).upper() == "HIGH")
        lines.append(f"{total} donor screening report(s) wait for review at {s.get('hospitalName', 'your hospital')}, {mine} assigned to you"
                     + (f"; {high} flagged high risk, review those first." if high else "."))
    else:
        lines.append("No screening reports are waiting for review.")
    awaiting = int(s.get("approvedAwaitingDonation") or 0)
    if awaiting:
        lines.append(f"{awaiting} approved donor(s) have a reserved slot; record their donation when they have donated, or release the slot if they cannot attend.")
    requests = sorted(s.get("assignedRequestsAwaitingDecision") or [], key=lambda r: priority_score(r.get("priority", ""), r.get("unitsRequired", 1)), reverse=True)
    if requests:
        first = requests[0]
        lines.append(f"{len(requests)} blood request(s) assigned to you need a decision; start with the {first.get('priority')} {first.get('bloodGroup')} request.")
    return " ".join(lines), [{"label": "Screening queue", "route": "/doctor/screenings"}, {"label": "Doctor dashboard", "route": "/doctor/dashboard"}]


def _hospital_summary(s: Dict[str, Any]) -> Tuple[str, List[Dict[str, str]]]:
    lines: List[str] = []
    actions = [{"label": "Blood inventory", "route": "/hospital/inventory"}]
    inventory = s.get("inventory") or []
    low = [i for i in inventory if int(i.get("units") or 0) < int(i.get("threshold") or 0)]
    expiring = [i for i in inventory if int(i.get("expiringSoon") or 0) > 0]
    if low:
        lines.append("Below threshold: " + ", ".join(f"{i['bloodGroup']} ({i['units']}/{i['threshold']})" for i in low) + ". Consider a transfer request.")
        actions.append({"label": "Inter-hospital transfers", "route": "/hospital/transfers"})
    if expiring:
        lines.append(f"Expiring within {s.get('expiryAlertDays', 5)} day(s): " + ", ".join(f"{i['expiringSoon']} x {i['bloodGroup']}" for i in expiring)
                     + ". Use them first or offer them to another hospital.")
    if not low and not expiring:
        lines.append("All blood groups are at or above their thresholds and nothing is about to expire.")
    pending = int(s.get("pendingVerifications") or 0)
    if pending:
        lines.append(f"{pending} blood request(s) wait for verification.")
        actions.append({"label": "Verify blood requests", "route": "/hospital/requests/verify"})
    transfers = s.get("transfers") or {}
    if transfers.get("incomingPending"):
        lines.append(f"{transfers['incomingPending']} incoming transfer(s) wait for your response.")
    if s.get("approvedDonorsAwaitingDonation"):
        lines.append(f"{s['approvedDonorsAwaitingDonation']} approved donor(s) are due to donate.")
    return " ".join(lines), actions


def _admin_summary(s: Dict[str, Any]) -> Tuple[str, List[Dict[str, str]]]:
    return (f"{s.get('pendingHospitalRegistrations', 0)} hospital registration(s), {s.get('openComplaints', 0)} open complaint(s) "
            f"and {s.get('pendingAppeals', 0)} pending appeal(s) need attention."), [
        {"label": "Hospital registrations", "route": "/admin/hospitals/pending"},
        {"label": "Complaints hub", "route": "/admin/complaints"},
        {"label": "Appeals", "route": "/admin/appeals"}]


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()
