"""
Screening interview turn (LangGraph), 7 questions plus the confirmation tick.

    load -> interpret --answer--> record --(question still missing a part)--> END   (short follow-up, not a new question)
                     |                  +--(next question / confirmation)----> END
                     |                  +--(confirmed)---------------------> submit -> END
                     +--(all answered and confirmed, report not yet accepted by the backend)--> submit -> END
                     +--question--> explain  -> END   (Supervisor answers in simple words, then the question is repeated)
                     +--clarify---> clarify  -> END
                     +--withdraw--> withdraw -> END   (the agent never withdraws for the donor; it points to the button)
                     +--complete--> END                (already submitted)

Input is structured (the inputs inside the chat bubble) or free text. Free text is read by rules first, then by the LLM
extractor (never for the confidential question 7), and otherwise the agent asks for one part at a time.
The agent guides, explains, records answers and generates the report. It never approves or rejects a donor.
"""
from langgraph.graph import END, StateGraph

from graph.state import ScreeningTurnState
from models.donor_screening import CONFIRM_ID, NIC_REMINDER, QUESTIONS, QuestionDefinition, missing_parts
from services import answer_parser
from services.gemini_service import gemini_service
from services.screening_service import CONFIRM, screening_service


async def load_node(state: ScreeningTurnState):
    session, acceptance = await screening_service.get_or_start(state["db"], state["acceptance_id"])
    profile = await screening_service.profile(session)
    question = screening_service.current_question(session, profile)
    return {"session": session, "acceptance": acceptance, "profile": profile, "question": question}


def _ask_text(question, follow_up_parts=None) -> str:
    if question == CONFIRM:
        return "That's all 7 questions. Please tick \"I confirm my answers are true\" to send your answers to the doctor."
    if follow_up_parts:
        return "Thank you. I just need a little more: " + "; ".join(p.label for p in follow_up_parts) + "."
    return f"Question {question.number} of {len(QUESTIONS)}: {question.text}"


async def interpret_node(state: ScreeningTurnState):
    session, profile, question = state["session"], state["profile"], state.get("question")
    if session.status == "Submitted":
        return {"kind": "complete", "reply": "Your screening answers have been sent to the doctor. You can follow the decision in My Acceptances."}
    if question is None:
        # Every answer is saved and confirmed but the report did not reach the backend last time: submit now
        return {"kind": "submit"}

    structured = state.get("structured") or {}
    message = (state.get("message") or "").strip()

    if question == CONFIRM:
        ticked = str(structured.get(CONFIRM_ID, "")).lower() in ("yes", "true")
        if not ticked and message:
            if answer_parser.WITHDRAW.search(message):
                return {"kind": "withdraw"}
            if answer_parser.is_question(message) and answer_parser.parse_yes_no(message) is None:
                return {"kind": "question", "query": message}
            ticked = answer_parser.parse_yes_no(message) == "Yes"
        if ticked:
            return {"kind": "answer", "values": {CONFIRM_ID: "Yes"}}
        return {"kind": "clarify", "hint": "Please tick \"I confirm my answers are true\" when you are ready to send your answers."}

    answers = screening_service.answer_map(session)
    missing = missing_parts(question, profile, answers)

    if structured:
        values, errors = answer_parser.parse_structured(question, structured)
        if errors and not values:
            return {"kind": "clarify", "hint": " ".join(errors)}
        return {"kind": "answer", "values": values, "errors": errors}

    parsed = answer_parser.parse_text(message, missing, screening_service.defaults(session, profile, question))
    if parsed.kind == "extract":
        if not question.confidential:
            # The LLM reads the reply into fields; every value is validated by the same rules as the inputs
            extracted = await gemini_service.extract_answers(question.text, [p.model_dump() for p in missing], message)
            values, _ = answer_parser.parse_structured(question, {k: v for k, v in extracted.items() if k in {p.id for p in missing}})
            if values:
                return {"kind": "answer", "values": values}
        first = missing[0]
        return {"kind": "clarify", "hint": f"Let's take it one part at a time. {first.label}?"}
    if parsed.kind == "question":
        return {"kind": "question", "query": parsed.query}
    if parsed.kind in ("clarify", "withdraw"):
        return {"kind": parsed.kind, "hint": parsed.hint}
    return {"kind": "answer", "values": parsed.values}


def route(state: ScreeningTurnState) -> str:
    return state.get("kind", "clarify")


async def record_node(state: ScreeningTurnState):
    db, session, profile, question = state["db"], state["session"], state["profile"], state["question"]
    screening_service.save_values(db, session, state.get("values") or {}, profile)
    errors = state.get("errors") or []

    if question == CONFIRM:
        return {"kind": "submit"}

    answers = screening_service.answer_map(session)
    still_missing = missing_parts(question, profile, answers)
    if still_missing:
        prefix = (" ".join(errors) + " ") if errors else ""
        return {"kind": "answer", "follow_up": True, "reply": prefix + _ask_text(question, still_missing)}

    next_question = screening_service.current_question(session, profile)
    if next_question is None:
        return {"kind": "submit"}
    return {"kind": "answer", "question": next_question, "reply": "Thank you. " + _ask_text(next_question)}


async def submit_node(state: ScreeningTurnState):
    await screening_service.submit(state["db"], state["session"], state["acceptance"])
    return {"kind": "complete",
            "reply": "Thank you, that's everything. Your answers have been sent to the doctor, who will review them and make the "
                     f"decision. You'll be notified, and you can follow it in My Acceptances. {NIC_REMINDER}"}


async def explain_node(state: ScreeningTurnState):
    question = state["question"]
    if isinstance(question, QuestionDefinition) and question.confidential:
        # Confidential question: the knowledge base is searched with a fixed topic, never with the donor's own words
        query = "Why are blood donors asked about pregnancy and risk behaviours before donating?"
    else:
        topic = question.text if isinstance(question, QuestionDefinition) else "confirming screening answers"
        query = f"{state['query']} (blood donor screening question: {topic})"
    return {"query": query, "reply": "When you're ready: " + _ask_text(question)}


async def clarify_node(state: ScreeningTurnState):
    hint = state.get("hint") or ""
    return {"reply": hint if hint.endswith("?") else f"{hint} {_ask_text(state['question'])}".strip()}


async def withdraw_node(state: ScreeningTurnState):
    return {"reply": "If you no longer wish to donate, use the Withdraw button in My Acceptances; your answers are kept "
                     "if you change your mind. Otherwise: " + _ask_text(state["question"])}


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
