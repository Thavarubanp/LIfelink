"""
Nodes of the Supervisor graph.

Worker agents (remote): Request Management (screening), Notification, Inventory.
Supervisor capabilities (in-process): planning (prioritisation / next steps) and knowledge (RAG).
"""
import logging
from typing import Any, Dict, List

from config.settings import settings
from graph.state import SupervisorState
from knowledge.store import knowledge_store
from services import guardrails, intents, llm, planner
from services.agent_clients import agent_clients

logger = logging.getLogger("Supervisor.Nodes")

EVENT_PLANS = {
    "BloodRequestApproved": ["planning", "notification"],
    "DonorAccepted": ["request_management"],
    "EmergencyShortage": ["planning", "inventory", "notification"],
    "InventoryCheck": ["inventory", "planning", "notification"],
}


def _add(state: SupervisorState, key: str, value: Any) -> List[Any]:
    return list(state.get(key) or []) + ([value] if not isinstance(value, list) else value)


def _record(state: SupervisorState, agent: str, result: Dict[str, Any]) -> Dict[str, Any]:
    results = dict(state.get("results") or {})
    results[agent] = result
    update: Dict[str, Any] = {"results": results, "agents_invoked": _add(state, "agents_invoked", agent)}
    if not result.get("success", False):
        update["errors"] = _add(state, "errors", {"agent": agent, "error": str(result.get("error", "failed"))})
    return update


# ---------------------------------------------------------------- entry and routing

async def ingest_node(state: SupervisorState) -> Dict[str, Any]:
    if state.get("kind") == "event":
        event = state.get("event_type", "")
        queue = list(EVENT_PLANS.get(event, []))
        trace = [f"event {event} -> {queue or 'no workflow'}"]
        errors = [] if queue else [{"agent": "Supervisor", "error": f"Unsupported event type '{event}'."}]
        return {"queue": queue, "trace": trace, "errors": errors, "steps": 0, "results": {}, "agents_invoked": []}

    chat = state.get("chat") or {}
    if chat.get("mode") == "screening":
        return {"queue": ["request_management"], "intents": ["screening"], "trace": ["chat screening turn"], "steps": 0,
                "results": {}, "agents_invoked": [], "segments": [], "actions": [], "knowledge_queries": []}

    message = (chat.get("message") or "").strip()
    found = await intents.classify(message, (chat.get("user") or {}).get("role", "Donor")) if message else ["account"]
    queue: List[str] = []
    queries: List[Dict[str, str]] = []
    if "account" in found:
        queue.append("planning")
    for domain in ("platform", "medical"):
        if domain in found:
            queries.append({"domain": domain, "query": message})
    if queries:
        queue.append("knowledge")
    return {"intents": found, "queue": queue, "knowledge_queries": queries, "trace": [f"chat intents {found}"],
            "steps": 0, "results": {}, "agents_invoked": [], "segments": [], "actions": []}


async def supervisor_node(state: SupervisorState) -> Dict[str, Any]:
    """Decides which worker runs next; hands over to compose when the plan is done or the step budget is used."""
    queue = list(state.get("queue") or [])
    steps = int(state.get("steps") or 0)
    if not queue or steps >= settings.MAX_SUPERVISOR_STEPS:
        return {"next": "compose", "queue": []}
    step = queue.pop(0)
    return {"next": step, "queue": queue, "steps": steps + 1, "trace": _add(state, "trace", f"supervisor -> {step}")}


def route(state: SupervisorState) -> str:
    return state.get("next") or "compose"


# ---------------------------------------------------------------- planning (in-process)

async def planning_node(state: SupervisorState) -> Dict[str, Any]:
    if state.get("kind") == "chat":
        chat = state.get("chat") or {}
        text, actions = planner.account_summary((chat.get("user") or {}).get("role", "Donor"), chat.get("snapshot") or {})
        return {"segments": _add(state, "segments", {"type": "account", "text": text, "sources": []}),
                "actions": _add(state, "actions", actions), "agents_invoked": _add(state, "agents_invoked", "Planning")}

    event, payload = state.get("event_type"), state.get("payload") or {}
    if event == "BloodRequestApproved":
        plan = planner.triage_request(payload)
    elif event == "EmergencyShortage":
        plan = planner.triage_emergency(payload)
    else:  # InventoryCheck: keep the most important alerts per hospital
        recommendations = ((state.get("results") or {}).get("InventoryAgent") or {}).get("data", {}).get("recommendations", [])
        kept = planner.prioritise_alerts(recommendations)
        plan = {"recommendations": kept, "summary": f"{len(kept)} of {len(recommendations)} inventory recommendation(s) kept after prioritisation."}
    update = _record(state, "Planning", {"success": True, "data": plan})
    update["trace"] = _add(state, "trace", f"planning: {plan.get('summary', '')}")
    return update


# ---------------------------------------------------------------- knowledge / RAG (in-process)

KNOWLEDGE_SYSTEM = (
    "You are the LifeLink knowledge assistant for a blood donation platform. Answer ONLY from the numbered passages. "
    "Cite passages like [1]. If the passages do not answer the question, say that you do not have verified guidance "
    "on it. Never approve or reject a donor or make a medical decision; the doctor decides. Use plain language and "
    "at most 150 words.")

NO_ANSWER = "I don't have verified guidance on that in the LifeLink knowledge base. Please ask the blood bank doctor or hospital staff."


async def answer_from_knowledge(domain: str, question: str) -> Dict[str, Any]:
    chunks = knowledge_store.search(domain, question)
    if not chunks:
        return {"type": domain, "text": NO_ANSWER, "sources": []}

    sources = []
    for chunk in chunks:
        source = chunk.source()
        if source not in sources:
            sources.append(source)

    passages = "\n\n".join(f"[{i + 1}] ({c.title}{' - ' + c.section if c.section else ''})\n{c.text}" for i, c in enumerate(chunks))
    text = await llm.complete(KNOWLEDGE_SYSTEM, f"Question: {question}\n\nPassages:\n{passages}")
    if not text:  # extractive fallback without Gemini: quote the best passage and cite only it
        best = chunks[0].text.strip()
        text = (best[:700].rsplit("\n", 1)[0] + "\n...") if len(best) > 700 else best
        text += " [1]"
        sources = sources[:1]
    if domain == "medical":
        text = guardrails.with_medical_disclaimer(text)
    return {"type": domain, "text": text, "sources": sources if domain == "medical" else sources[:1]}


async def knowledge_node(state: SupervisorState) -> Dict[str, Any]:
    segments = list(state.get("segments") or [])
    for item in state.get("knowledge_queries") or []:
        segments.append(await answer_from_knowledge(item["domain"], item["query"]))
    return {"segments": segments, "knowledge_queries": [], "agents_invoked": _add(state, "agents_invoked", "Knowledge")}


# ---------------------------------------------------------------- remote workers

async def request_management_node(state: SupervisorState) -> Dict[str, Any]:
    if state.get("kind") == "event":
        acceptance_id = str((state.get("payload") or {}).get("acceptanceId") or "")
        if not acceptance_id:
            return _record(state, "RequestManagementAgent", {"success": False, "error": "Missing acceptanceId."})
        return _record(state, "RequestManagementAgent", await agent_clients.screening_start(acceptance_id))

    chat = state.get("chat") or {}
    acceptance_id = str(chat.get("acceptanceId") or "")
    message = (chat.get("message") or "").strip()
    result = await (agent_clients.screening_turn(acceptance_id, message) if message else agent_clients.screening_session(acceptance_id))
    update = _record(state, "RequestManagementAgent", result)
    if not result.get("success"):
        update["segments"] = _add(state, "segments", {"type": "screening", "sources": [],
                                                      "text": "The screening assistant is not available right now. Your answers so far are saved; please try again shortly."})
        return update

    data = result.get("data") or {}
    update["screening"] = data.get("screening") or {}
    if data.get("kind") == "question" and data.get("query"):
        # The donor asked something mid-interview: explain it from the knowledge base, then repeat the question
        update["knowledge_queries"] = [{"domain": "medical", "query": data["query"]}]
        update["queue"] = ["knowledge"] + list(state.get("queue") or [])
        update["pending_screening_reply"] = data.get("reply") or ""
    else:
        update["segments"] = _add(state, "segments", {"type": "screening", "text": data.get("reply") or "", "sources": []})
    if data.get("kind") == "withdraw" and acceptance_id:
        update["actions"] = _add(state, "actions", {"label": "Withdraw from this donation", "route": "/donor/acceptances"})
    return update


async def notification_node(state: SupervisorState) -> Dict[str, Any]:
    event, payload = state.get("event_type"), state.get("payload") or {}
    results = state.get("results") or {}

    if event == "BloodRequestApproved":
        result = await agent_clients.donor_alerts({
            "request_id": str(payload.get("requestId") or ""),
            "blood_group": payload.get("bloodGroup") or "",
            "units_required": int(payload.get("unitsRequired") or 1),
            "priority": payload.get("priority") or "Normal",
            "hospital_id": str(payload.get("hospitalId") or ""),
            "hospital_name": payload.get("hospitalName") or "Partner Hospital",
            "patient_reason": "Approved blood request",
            "available_donors": payload.get("availableDonors") or [],
            "verified_hospital_ids": payload.get("verifiedHospitalIds") or [],
        })
        notifications = [{
            "recipientType": n.get("recipient_type", ""),
            "recipientId": n.get("recipient_id") or "",
            "notificationType": "UrgentHospitalAlert" if n.get("recipient_type") == "Hospital" else "EligibleDonorAlert",
            "title": n.get("title", ""), "message": n.get("message", ""),
        } for n in (result.get("data") or {}).get("notifications", [])] if result.get("success") else []
    else:
        if event == "EmergencyShortage":
            holders = ((results.get("InventoryAgent") or {}).get("data") or {}).get("recommendations") or []
        else:
            holders = ((results.get("Planning") or {}).get("data") or {}).get("recommendations") or []
        if not holders:
            update = _record(state, "NotificationAgent", {"success": True, "data": {"notifications": []}})
            update["notifications"] = []
            return update
        result = await agent_clients.hospital_alerts(holders, {"event": event, "hospitalName": payload.get("hospitalName"),
                                                               "bloodGroup": payload.get("bloodGroup"), "priority": payload.get("priority"),
                                                               "unitsRequired": payload.get("unitsRequired")})
        notifications = [{
            "recipientType": "Hospital",
            "recipientId": n.get("recipient_id") or "",
            "notificationType": n.get("notification_type") or "InventoryAlert",
            "title": n.get("title", ""), "message": n.get("message", ""),
        } for n in (result.get("data") or {}).get("notifications", [])] if result.get("success") else []

    update = _record(state, "NotificationAgent", result)
    update["notifications"] = notifications
    update["trace"] = _add(state, "trace", f"notification: {len(notifications)} alert(s) composed")
    return update


async def inventory_node(state: SupervisorState) -> Dict[str, Any]:
    event, payload = state.get("event_type"), state.get("payload") or {}
    if event == "EmergencyShortage":
        body = {"mode": "emergency", "emergency": {
            "hospital_id": payload.get("hospitalId"), "hospital_name": payload.get("hospitalName"),
            "blood_group": payload.get("bloodGroup"), "units_required": payload.get("unitsRequired"),
            "priority": payload.get("priority")}, "stock_hospitals": payload.get("stockHospitals") or []}
    else:
        body = {"mode": "monitor", "inventories": payload.get("inventories") or [], "public_requests": payload.get("publicRequests") or []}
    result = await agent_clients.inventory_analyze(body)
    update = _record(state, "InventoryAgent", result)
    count = len((result.get("data") or {}).get("recommendations") or [])
    update["trace"] = _add(state, "trace", f"inventory: {count} recommendation(s)")
    return update


# ---------------------------------------------------------------- compose and guard

async def compose_node(state: SupervisorState) -> Dict[str, Any]:
    if state.get("kind") == "event":
        return {}

    segments = list(state.get("segments") or [])
    pending = state.get("pending_screening_reply")
    if pending:
        segments.append({"type": "screening", "text": pending, "sources": []})
    if not segments:
        segments.append({"type": "platform", "sources": [],
                         "text": "I can explain blood donation guidance, how LifeLink works, or summarise your own requests and donations. What would you like to know?"})
    # Account first, then LifeLink guidance, then medical guidance, then the screening question
    order = {"account": 0, "platform": 1, "medical": 2, "screening": 3}
    segments.sort(key=lambda s: order.get(s.get("type", ""), 9))
    return {"segments": segments}


async def guard_node(state: SupervisorState) -> Dict[str, Any]:
    if state.get("kind") == "event":
        return {}
    snapshot = (state.get("chat") or {}).get("snapshot") or {}
    screening_text = str(state.get("screening") or "")
    segments = []
    for segment in state.get("segments") or []:
        text = guardrails.enforce_authority(guardrails.redact(segment.get("text", ""), snapshot, screening_text))
        segments.append({**segment, "text": text})
    actions = []
    seen = set()
    for action in state.get("actions") or []:
        if action.get("route", "").startswith("/") and action["route"] not in seen:
            seen.add(action["route"])
            actions.append(action)
    return {"segments": segments, "actions": actions[:4], "reply": "\n\n".join(s["text"] for s in segments if s.get("text"))}
