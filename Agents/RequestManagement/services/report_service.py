"""
Builds the structured screening report (one immutable version per submission), schema lifelink.screening.v2.

The doctor's view is unchanged: risk level, AI recommendation, summary, the flags box and one section per question
(question 7 is marked confidential). Two extra keys serve the donor and are not shown to the doctor:
  questionnaire - the 7 questions with their parts, so the edit form shows exactly what was asked;
  form_answers  - the stored field values, used to prefill the edit form and to show the donor what they submitted.
"""
import json
from datetime import datetime, timezone
from typing import Any, Dict, List

from models.donor_screening import CONFIRM_TEXT, PARTS_BY_ID, QUESTIONS, questionnaire_schema
from services.answer_parser import display_value

SCHEMA = "lifelink.screening.v2"


def build_sections(answers: Dict[str, str], evaluation: Dict[str, Any]) -> List[Dict[str, Any]]:
    flags_by_question: Dict[int, List[str]] = {}
    for f in evaluation["flags"]:
        flags_by_question.setdefault(int(f["section"]), []).append(f["message"])

    sections = []
    for q in QUESTIONS:
        items = [{"question_id": p.id, "question": p.label, "answer": display_value(p, answers[p.id])}
                 for p in q.parts if p.id in answers]
        if q.number == 1 and evaluation.get("age") is not None:
            items.insert(2, {"question_id": "P_AGE", "question": "Age", "answer": str(evaluation["age"])})
        sections.append({"index": q.number, "title": q.title, "confidential": q.confidential,
                         "items": items, "flags": flags_by_question.get(q.number, [])})
    return sections


def deidentified_answers(answers: Dict[str, str]) -> List[Dict[str, str]]:
    """Answers safe to share with the summarising LLM: no personal details (question 1, contact) and never question 7."""
    hidden = {"P_NAME", "P_DOB", "P_GENDER", "P_CONTACT"}
    result = []
    for q in QUESTIONS:
        if q.confidential:
            continue
        for p in q.parts:
            if p.id in answers and p.id not in hidden:
                result.append({"question": p.label, "answer": display_value(p, answers[p.id])})
    return result


def donor_view(answers: Dict[str, str]) -> List[Dict[str, Any]]:
    """What the donor submitted, question by question (no AI fields)."""
    return [{"number": q.number, "title": q.title, "text": q.text, "confidential": q.confidential,
             "items": [{"question": p.label, "answer": display_value(p, answers[p.id])} for p in q.parts if p.id in answers]}
            for q in QUESTIONS]


def build_report(*, report_id: str, acceptance: Dict[str, Any], version: int, answers: Dict[str, str],
                 evaluation: Dict[str, Any], summary: Dict[str, str]) -> Dict[str, Any]:
    def a(field: str) -> str:
        return display_value(PARTS_BY_ID.get(field), answers.get(field)) if field in answers else ""

    return {
        "schema": SCHEMA,
        "report_id": report_id,
        "report_version": version,
        "acceptance_id": str(acceptance.get("acceptanceId")),
        "blood_request_id": str(acceptance.get("bloodRequestId")),
        "donor_user_id": str(acceptance.get("donorUserId")),
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "risk_level": evaluation["risk_level"],
        "recommendation": evaluation["recommendation"],
        "summary": summary["summary"],
        "doctor_notes": summary.get("doctor_notes", ""),
        "flags": evaluation["flags"],
        "donor": {
            "full_name": a("P_NAME"), "date_of_birth": a("P_DOB"), "age": evaluation.get("age"), "gender": a("P_GENDER"),
            "weight": a("P_WEIGHT_KG"), "contact_number": a("P_CONTACT"),
        },
        "sections": build_sections(answers, evaluation),
        "confirmation": CONFIRM_TEXT,
        "questionnaire": questionnaire_schema(),
        "form_answers": answers,
        "governance": "AI-assisted summary and rule-based flags only. The reviewing doctor makes the final decision.",
    }


def to_json(report: Dict[str, Any]) -> str:
    return json.dumps(report, ensure_ascii=False)
