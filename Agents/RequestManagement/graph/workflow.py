"""
Screening interview turn (LangGraph).

    load -> interpret --answer--> record --(more questions)--> END
                     |                  +--(last answer)---> submit -> END
                     +--(all answered, report not yet accepted by the backend)--> submit -> END
                     +--question--> explain  -> END   (Supervisor adds a knowledge-base answer, then repeats the question)
                     +--clarify---> clarify  -> END
                     +--withdraw--> withdraw -> END   (the agent never withdraws for the donor; it points to the button)
                     +--complete--> END                (already submitted)

The agent guides, explains, records answers and generates the report. It never approves or rejects a donor.
"""
from langgraph.graph import END, StateGraph

from graph.state import ScreeningTurnState
from models.donor_screening import SECTIONS, render_question
from services import answer_parser
from services.screening_service import screening_service


async def load_node(state: ScreeningTurnState):
    session, acceptance = await screening_service.get_or_start(state["db"], state["acceptance_id"])
    profile = await screening_service.profile(session)
    question, previous = screening_service.current_question(session, profile)
    return {"session": session, "acceptance": acceptance, "profile": profile, "question": question, "previous": previous}


async def interpret_node(state: ScreeningTurnState):
    if state["session"].status == "Submitted":
        return {"kind": "complete", "reply": "Your screening report has been submitted to the doctor. You can follow the decision in My Acceptances."}
    if state.get("question") is None:
        # Every answer is saved but the report did not reach the backend last time (it was unavailable): submit now
        return {"kind": "submit"}
    parsed = answer_parser.parse(state.get("message", ""), state["question"], state["profile"], state.get("previous"))
    return {"parsed": parsed, "kind": parsed.kind}


def route(state: ScreeningTurnState) -> str:
    return state.get("kind", "clarify")


def _ask(state: ScreeningTurnState, question, previous=None, prefix: str = "") -> str:
    rendered = render_question(question, state["profile"], previous)
    header = ""
    current = state.get("question")
    if current is None or question.section_index != current.section_index:
        header = f"Section {question.section_index} of {len(SECTIONS)}: {question.section}. "
    return f"{prefix}{header}{rendered['text']}".strip()


async def record_node(state: ScreeningTurnState):
    db, session, question = state["db"], state["session"], state["question"]
    screening_service.save_answer(db, session, question, state["parsed"].value)
    next_question, previous = screening_service.current_question(session, state["profile"])
    if next_question is None:
        return {"kind": "submit"}
    return {"kind": "answer", "reply": _ask(state, next_question, previous, prefix="Thank you. ")}


async def submit_node(state: ScreeningTurnState):
    await screening_service.submit(state["db"], state["session"], state["acceptance"])
    return {"kind": "complete",
            "reply": "Thank you, that's everything. Your screening report has been sent to the doctor, who will review it "
                     "and make the decision. You'll be notified, and you can follow it in My Acceptances."}


async def explain_node(state: ScreeningTurnState):
    question = state["question"]
    # Confidential section: the knowledge base is searched with a fixed topic, never with the donor's own words
    query = (f"Why are blood donors asked about {question.section.lower()}?" if question.confidential
             else f"{state['parsed'].query} (blood donor screening question: {question.question_text.replace('{value}', '')})")
    help_text = f"{question.help} " if question.help else ""
    return {"query": query, "reply": _ask(state, question, state.get("previous"), prefix=f"{help_text}When you're ready: ")}


async def clarify_node(state: ScreeningTurnState):
    return {"reply": _ask(state, state["question"], state.get("previous"), prefix=f"{state['parsed'].hint} ")}


async def withdraw_node(state: ScreeningTurnState):
    return {"reply": "If you no longer wish to donate, use the Withdraw button in My Acceptances; your answers are kept "
                     "if you change your mind. " + _ask(state, state["question"], state.get("previous"), prefix="Otherwise: ")}


def build_turn_graph():
    graph = StateGraph(ScreeningTurnState)
    graph.add_node("load", load_node)
    graph.add_node("interpret", interpret_node)
    graph.add_node("record", record_node)
    graph.add_node("submit", submit_node)
    graph.add_node("explain", explain_node)
    graph.add_node("clarify", clarify_node)
    graph.add_node("withdraw", withdraw_node)

    graph.set_entry_point("load")
    graph.add_edge("load", "interpret")
    graph.add_conditional_edges("interpret", route, {
        "answer": "record", "question": "explain", "clarify": "clarify", "withdraw": "withdraw", "submit": "submit", "complete": END})
    graph.add_conditional_edges("record", route, {"submit": "submit", "answer": END})
    for node in ("submit", "explain", "clarify", "withdraw"):
        graph.add_edge(node, END)
    return graph.compile()


screening_turn_graph = build_turn_graph()
