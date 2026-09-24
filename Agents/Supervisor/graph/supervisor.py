"""
LifeLink Supervisor graph (LangGraph).

    ingest -> supervisor --(next step)--> planning | knowledge | request_management | notification | inventory
                 ^                                              |
                 +--------------- every worker returns ---------+
    supervisor --(plan done)--> compose -> guard -> END

The supervisor node owns routing and state; workers do one job each and report back. Workers can add
follow-up steps (a screening question that needs the knowledge base), so routing is decided at run time.
"""
from langgraph.graph import END, StateGraph

from graph import nodes
from graph.state import SupervisorState

WORKERS = ["planning", "knowledge", "request_management", "notification", "inventory"]


def build_supervisor_graph():
    graph = StateGraph(SupervisorState)
    graph.add_node("ingest", nodes.ingest_node)
    graph.add_node("supervisor", nodes.supervisor_node)
    graph.add_node("planning", nodes.planning_node)
    graph.add_node("knowledge", nodes.knowledge_node)
    graph.add_node("request_management", nodes.request_management_node)
    graph.add_node("notification", nodes.notification_node)
    graph.add_node("inventory", nodes.inventory_node)
    graph.add_node("compose", nodes.compose_node)
    graph.add_node("guard", nodes.guard_node)

    graph.set_entry_point("ingest")
    graph.add_edge("ingest", "supervisor")
    graph.add_conditional_edges("supervisor", nodes.route, {name: name for name in WORKERS + ["compose"]})
    for worker in WORKERS:
        graph.add_edge(worker, "supervisor")
    graph.add_edge("compose", "guard")
    graph.add_edge("guard", END)
    return graph.compile()


supervisor_graph = build_supervisor_graph()
