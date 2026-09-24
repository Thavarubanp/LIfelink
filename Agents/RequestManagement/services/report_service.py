"""Builds the structured screening report (one immutable version per submission)."""
import json
from datetime import datetime, timezone
from typing import Any, Dict, List

from models.donor_screening import QUESTION_BANK, QUESTIONS_BY_ID, SECTIONS
from services.answer_parser import display_answer

SECTION1_LABELS = {
    "P_NAME": "Full name", "P_NIC": "NIC / Passport number", "P_DOB": "Date of birth", "P_GENDER": "Gender",
    "P_ADDRESS": "Address", "P_MOBILE": "Mobile number", "P_EMAIL": "Email", "P_BLOOD_GROUP": "Blood group (if known)",
    "P_OCCUPATION": "Occupation", "P_EMERGENCY_NAME": "Emergency contact name", "P_EMERGENCY_PHONE": "Emergency contact number",
}
FEMALE_SECTION4_IDS = ("BE_PREGNANT", "BE_BREASTFEEDING", "BE_ABORTION_6M")


def _label(question_id: str) -> str:
    if question_id in SECTION1_LABELS:
        return SECTION1_LABELS[question_id]
    return QUESTIONS_BY_ID[question_id].question_text


def build_sections(answers: Dict[str, str], evaluation: Dict[str, Any]) -> List[Dict[str, Any]]:
    flags_by_section: Dict[int, List[str]] = {}
    for f in evaluation["flags"]:
        flags_by_section.setdefault(int(f["section"]), []).append(f["message"])

    sections = []
    for index, title in SECTIONS.items():
        items = []
        for q in QUESTION_BANK:
            if q.section_index != index or q.question_id not in answers:
                continue
            items.append({"question_id": q.question_id, "question": _label(q.question_id),
                          "answer": display_answer(q, answers[q.question_id])})
        if index == 1 and evaluation.get("age") is not None:
            items.insert(3, {"question_id": "P_AGE", "question": "Age", "answer": str(evaluation["age"])})
        if index == 12:
            # Pregnancy questions are asked once in Section 4 and repeated here for the doctor
            items = [{"question_id": qid, "question": QUESTIONS_BY_ID[qid].question_text,
                      "answer": answers[qid]} for qid in FEMALE_SECTION4_IDS if qid in answers] + items
        sections.append({"index": index, "title": title, "confidential": index == 10,
                         "items": items, "flags": flags_by_section.get(index, [])})
    return sections


def deidentified_answers(answers: Dict[str, str]) -> List[Dict[str, str]]:
    """Answers safe to share with the summarising LLM: no personal details (Section 1), no confidential Section 10."""
    result = []
    for q in QUESTION_BANK:
        if q.section_index in (1, 10) or q.question_id not in answers:
            continue
        result.append({"section": q.section, "question": q.question_text, "answer": display_answer(q, answers[q.question_id])})
    return result


def build_report(*, report_id: str, acceptance: Dict[str, Any], version: int, answers: Dict[str, str],
                 evaluation: Dict[str, Any], summary: Dict[str, str]) -> Dict[str, Any]:
    def a(qid: str) -> str:
        return display_answer(QUESTIONS_BY_ID[qid], answers.get(qid)) if qid in answers else ""

    return {
        "schema": "lifelink.screening.v1",
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
            "full_name": a("P_NAME"), "nic": a("P_NIC"), "date_of_birth": a("P_DOB"), "age": evaluation.get("age"),
            "gender": a("P_GENDER"), "address": a("P_ADDRESS"), "mobile": a("P_MOBILE"), "email": a("P_EMAIL"),
            "blood_group": a("P_BLOOD_GROUP"), "occupation": a("P_OCCUPATION"),
            "emergency_contact_name": a("P_EMERGENCY_NAME"), "emergency_contact_phone": a("P_EMERGENCY_PHONE"),
        },
        "sections": build_sections(answers, evaluation),
        "governance": "AI-assisted summary and recommendation only. The reviewing doctor makes the final decision.",
    }


def to_json(report: Dict[str, Any]) -> str:
    return json.dumps(report, ensure_ascii=False)
