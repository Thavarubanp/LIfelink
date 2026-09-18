import logging
from typing import Any, Dict, List
from graph.state import AgentState
from services.agent_clients import agent_clients

logger = logging.getLogger("PlanningAgent.Nodes")


async def receive_request_node(state: AgentState) -> Dict[str, Any]:
    """
    Node 1: receive_request_node
    Reads incoming event type and contextual payload.
    Initializes execution state tracking.
    """
    event_type = state.get("event_type", "").strip()
    payload = state.get("payload", {})

    logger.info(f"[receive_request_node] Received event '{event_type}' with {len(payload)} payload keys.")

    return {
        "event_type": event_type,
        "payload": payload,
        "agents_invoked": [],
        "errors": state.get("errors", []) or [],
        "status": "PROCESSING"
    }


async def determine_workflow_node(state: AgentState) -> Dict[str, Any]:
    """
    Node 2: determine_workflow_node
    Purely deterministic decision engine.
    Maps event types to ordered agent invocation sequences without LLM dependency.
    """
    event_type = state.get("event_type", "")
    logger.info(f"[determine_workflow_node] Routing event: {event_type}")

    if event_type == "BloodRequestApproved":
        return {
            "workflow_name": "BloodRequestApproved",
            "agents_to_invoke": ["MatchingAgent"]
        }

    elif event_type == "DonorAccepted":
        return {
            "workflow_name": "DonorAccepted",
            "agents_to_invoke": ["ScreeningAgent"]
        }

    elif event_type == "EmergencyShortage":
        return {
            "workflow_name": "EmergencyShortage",
            "agents_to_invoke": ["InventoryAgent", "MatchingAgent"]
        }

    else:
        logger.warning(f"[determine_workflow_node] Unrecognized event type '{event_type}'")
        return {
            "workflow_name": "UnknownWorkflow",
            "agents_to_invoke": [],
            "errors": state.get("errors", []) + [{
                "agent": "PlanningAgent",
                "error": f"Unsupported event type: '{event_type}'. Supported: BloodRequestApproved, DonorAccepted, EmergencyShortage"
            }]
        }


async def invoke_screening_agent_node(state: AgentState) -> Dict[str, Any]:
    """
    Node 3: invoke_screening_agent_node
    Calls Agent 1 (Donor Screening Agent) REST API.
    """
    payload = state.get("payload", {})
    acceptance_id = str(payload.get("acceptanceId") or payload.get("acceptance_id") or "")

    logger.info(f"[invoke_screening_agent_node] Calling Agent 1 for acceptanceId: {acceptance_id}")

    if not acceptance_id:
        error = {"agent": "ScreeningAgent", "error": "Missing required field 'acceptanceId' in payload."}
        logger.error(error["error"])
        return {
            "agents_invoked": state.get("agents_invoked", []) + ["ScreeningAgent"],
            "screening_result": None,
            "errors": state.get("errors", []) + [error]
        }

    result = await agent_clients.call_screening_agent(acceptance_id, payload)
    errors = list(state.get("errors", []))

    if not result.get("success"):
        errors.append({"agent": "ScreeningAgent", "error": result.get("error", "Unknown error")})

    return {
        "agents_invoked": state.get("agents_invoked", []) + ["ScreeningAgent"],
        "screening_result": result,
        "errors": errors
    }


async def invoke_matching_agent_node(state: AgentState) -> Dict[str, Any]:
    """
    Node 4: invoke_matching_agent_node
    Calls Agent 2 (Notification & Matching Agent) REST API.
    """
    payload = state.get("payload", {})
    logger.info(f"[invoke_matching_agent_node] Calling Agent 2 for request: {payload.get('requestId') or payload.get('request_id')}")

    result = await agent_clients.call_matching_agent(payload)
    errors = list(state.get("errors", []))

    if not result.get("success"):
        errors.append({"agent": "MatchingAgent", "error": result.get("error", "Unknown error")})

    return {
        "agents_invoked": state.get("agents_invoked", []) + ["MatchingAgent"],
        "matching_result": result,
        "errors": errors
    }


async def invoke_inventory_agent_node(state: AgentState) -> Dict[str, Any]:
    """
    Node 5: invoke_inventory_agent_node
    Calls Agent 3 (Inventory Management Agent) REST API.
    """
    payload = state.get("payload", {})
    logger.info(f"[invoke_inventory_agent_node] Calling Agent 3 for hospital: {payload.get('hospitalId') or payload.get('hospital_id')}")

    result = await agent_clients.call_inventory_agent(payload)
    errors = list(state.get("errors", []))

    if not result.get("success"):
        errors.append({"agent": "InventoryAgent", "error": result.get("error", "Unknown error")})

    return {
        "agents_invoked": state.get("agents_invoked", []) + ["InventoryAgent"],
        "inventory_result": result,
        "errors": errors
    }


async def aggregate_results_node(state: AgentState) -> Dict[str, Any]:
    """
    Node 6: aggregate_results_node
    Collects and normalizes raw outputs from all downstream microservices.
    """
    logger.info("[aggregate_results_node] Aggregating multi-agent results.")
    aggregated: Dict[str, Any] = {}

    if state.get("screening_result"):
        aggregated["screening"] = state["screening_result"]

    if state.get("matching_result"):
        aggregated["matching"] = state["matching_result"]

    if state.get("inventory_result"):
        aggregated["inventory"] = state["inventory_result"]

    return {
        "execution_plan": {
            "rawResults": aggregated
        }
    }


async def generate_execution_plan_node(state: AgentState) -> Dict[str, Any]:
    """
    Node 7: generate_execution_plan_node
    Synthesizes deterministic, structured action items and directives for the backend.
    """
    workflow_name = state.get("workflow_name", "UnknownWorkflow")
    errors = state.get("errors", [])
    raw_results = state.get("execution_plan", {}).get("rawResults", {})

    logger.info(f"[generate_execution_plan_node] Generating plan for '{workflow_name}' (errors={len(errors)})")

    action_items: List[Dict[str, Any]] = []
    summary = ""
    overall_status = "COMPLETED"

    # Determine overall status based on downstream agent outcomes
    if not raw_results:
        overall_status = "FAILED"
    else:
        all_succeeded = all(r.get("success", False) for r in raw_results.values())
        any_succeeded = any(r.get("success", False) for r in raw_results.values())
        if all_succeeded and not errors:
            overall_status = "COMPLETED"
        elif any_succeeded:
            overall_status = "PARTIAL_FAILURE"
        else:
            overall_status = "FAILED"

    # --- Workflow 1: BloodRequestApproved ---
    if workflow_name == "BloodRequestApproved":
        matching_res = raw_results.get("matching", {})
        if matching_res.get("success"):
            matching_data = matching_res.get("data", {})
            donors_count = len(matching_data.get("ranked_donors", []))
            notifs_count = len(matching_data.get("notifications", []))

            summary = f"Blood request approved. Agent 2 ranked {donors_count} compatible donors and synthesized {notifs_count} notifications."
            action_items.append({
                "agent": "MatchingAgent",
                "action": "DISPATCH_DONOR_NOTIFICATIONS",
                "status": "SUCCESS",
                "details": {
                    "rankedDonorsCount": donors_count,
                    "notificationsGenerated": notifs_count,
                    "topDonors": matching_data.get("ranked_donors", [])[:5],
                    "notifications": matching_data.get("notifications", [])
                }
            })
        else:
            summary = "Blood request approved, but Agent 2 (Matching) failed to complete execution."
            action_items.append({
                "agent": "MatchingAgent",
                "action": "DISPATCH_DONOR_NOTIFICATIONS",
                "status": "FAILED",
                "details": {"error": matching_res.get("error", "Service unavailable")}
            })

    # --- Workflow 2: DonorAccepted ---
    elif workflow_name == "DonorAccepted":
        screening_res = raw_results.get("screening", {})
        if screening_res.get("success"):
            screening_data = screening_res.get("data", {})
            session_id = screening_data.get("session_id")
            first_q = screening_data.get("first_question", {})

            summary = f"Donor acceptance confirmed. Agent 1 initialized screening session '{session_id}'."
            action_items.append({
                "agent": "ScreeningAgent",
                "action": "LAUNCH_DONOR_SCREENING_SESSION",
                "status": "SUCCESS",
                "details": {
                    "sessionId": session_id,
                    "acceptanceId": screening_data.get("acceptance_id"),
                    "firstQuestion": first_q,
                    "status": screening_data.get("status")
                }
            })
        else:
            summary = "Donor acceptance recorded, but Agent 1 (Screening) failed to initialize session."
            action_items.append({
                "agent": "ScreeningAgent",
                "action": "LAUNCH_DONOR_SCREENING_SESSION",
                "status": "FAILED",
                "details": {"error": screening_res.get("error", "Service unavailable")}
            })

    # --- Workflow 3: EmergencyShortage ---
    elif workflow_name == "EmergencyShortage":
        inv_res = raw_results.get("inventory", {})
        matching_res = raw_results.get("matching", {})

        recommendations = inv_res.get("data", {}).get("recommendations", []) if inv_res.get("success") else []
        donors = matching_res.get("data", {}).get("ranked_donors", []) if matching_res.get("success") else []

        summary = (
            f"Emergency shortage mitigation plan generated: {len(recommendations)} inter-hospital transfer recommendations "
            f"and {len(donors)} emergency donors identified."
        )

        # Action 1: Inventory transfers
        action_items.append({
            "agent": "InventoryAgent",
            "action": "INITIATE_INTER_HOSPITAL_TRANSFERS",
            "status": "SUCCESS" if inv_res.get("success") else "FAILED",
            "details": {
                "recommendationsCount": len(recommendations),
                "recommendations": recommendations,
                "error": inv_res.get("error") if not inv_res.get("success") else None
            }
        })

        # Action 2: Direct donor emergency notification
        action_items.append({
            "agent": "MatchingAgent",
            "action": "BROADCAST_EMERGENCY_DONOR_ALERTS",
            "status": "SUCCESS" if matching_res.get("success") else "FAILED",
            "details": {
                "matchedDonorsCount": len(donors),
                "donors": donors[:5],
                "error": matching_res.get("error") if not matching_res.get("success") else None
            }
        })

    # --- Unknown or Unhandled Workflow ---
    else:
        err_details = [err.get("error") for err in errors if err.get("error")]
        err_str = "; ".join(err_details) if err_details else "unrecognized event"
        summary = f"Workflow '{workflow_name}' failed: {err_str}"
        for err in errors:
            action_items.append({
                "agent": err.get("agent", "PlanningAgent"),
                "action": "EXECUTION_ABORTED",
                "status": "FAILED",
                "details": {"error": err.get("error")}
            })

    execution_plan = {
        "summary": summary,
        "workflowStatus": overall_status,
        "actionItems": action_items,
        "results": raw_results
    }

    return {
        "execution_plan": execution_plan,
        "status": "COMPLETED" if overall_status != "FAILED" else "FAILED"
    }


async def return_response_node(state: AgentState) -> Dict[str, Any]:
    """
    Node 8: return_response_node
    Finalizes response envelope and returns execution state.
    """
    logger.info(f"[return_response_node] Execution completed. Final status: {state.get('status')}")
    return {
        "status": "DONE"
    }
