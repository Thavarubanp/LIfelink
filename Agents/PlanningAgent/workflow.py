import asyncio
import json
import sys
from graph.workflow import planning_agent_graph
from graph.state import AgentState

__all__ = ["planning_agent_graph"]


async def main():
    """CLI runner demonstrating workflow execution with sample events."""
    sample_request: AgentState = {
        "event_type": "BloodRequestApproved",
        "payload": {
            "requestId": "SAMPLE-REQ-001",
            "bloodGroup": "O+",
            "unitsRequired": 2,
            "priority": "URGENT",
            "hospitalName": "National Hospital"
        },
        "workflow_name": "",
        "agents_to_invoke": [],
        "agents_invoked": [],
        "screening_result": None,
        "matching_result": None,
        "inventory_result": None,
        "errors": [],
        "execution_plan": {},
        "status": "INIT"
    }

    print("Running sample planning agent invocation for 'BloodRequestApproved'...")
    final_state = await planning_agent_graph.ainvoke(sample_request)
    print("\n--- FINAL EXECUTION PLAN ---")
    print(json.dumps(final_state.get("execution_plan", {}), indent=2))


if __name__ == "__main__":
    asyncio.run(main())
