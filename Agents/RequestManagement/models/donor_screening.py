"""
LifeLink donor screening questionnaire: exactly 7 questions (Sri Lanka NBTS donor questionnaire, owner's Phase 5 list),
plus a final "I confirm my answers are true" tick that is not counted as a question.

Each question has typed parts (fields). The chat shows them as inputs inside the question bubble (tick boxes, options,
dates, numbers, "None of these", "I don't remember"), and free text is still accepted. Answers are stored per field ID,
and the deterministic rules (services/eligibility_rules.py) read those fields directly.

Part types:
  text, number, date        - one value ("date" is YYYY-MM-DD; parts with allow_unknown also accept "Donor doesn't remember")
  select                    - one of `options`
  yes_no                    - "Yes" / "No"
  checklist                 - any of `options`, or "None" (stored as a JSON list, [] = none of these)
  trips                     - list of {"country", "return_date"} (stored as JSON)

A part with `show_if` applies only when another field has a given answer (a ticked item or "Yes"); asking for it is a
short follow-up, not a new question. Question 7 is confidential: it is parsed by rules only and never sent to an LLM.
The pregnancy part is shown only to female donors.
"""
from typing import Any, Dict, List, Optional

from pydantic import BaseModel

YES_NO = ["Yes", "No"]
DONT_REMEMBER = "Donor doesn't remember"

CONDITIONS = ["Heart disease", "Diabetes", "High blood pressure", "Epilepsy", "Hepatitis", "Cancer", "Bleeding disorder"]
RECENT_EVENTS = ["Tattoo", "Surgery", "Blood transfusion"]
FEMALE_STATUS = ["Pregnant", "Breastfeeding", "Gave birth in the last year"]


class PartDefinition(BaseModel):
    id: str
    label: str
    type: str
    options: Optional[List[str]] = None
    required: bool = True
    allow_none: bool = False          # checklist: "None of these"
    allow_unknown: bool = False       # date: "I don't remember"
    prefill_key: Optional[str] = None  # profile value shown as the default
    show_if: Optional[Dict[str, str]] = None  # {"field": id, "equals": "Yes"} or {"field": id, "includes": "Tattoo"}
    female_only: bool = False
    min: Optional[float] = None
    max: Optional[float] = None


class QuestionDefinition(BaseModel):
    question_id: str
    number: int
    title: str
    text: str
    parts: List[PartDefinition]
    confidential: bool = False


def _p(pid: str, label: str, ptype: str, **kwargs) -> PartDefinition:
    if ptype == "yes_no":
        kwargs.setdefault("options", YES_NO)
    return PartDefinition(id=pid, label=label, type=ptype, **kwargs)


QUESTIONS: List[QuestionDefinition] = [
    QuestionDefinition(question_id="Q1", number=1, title="About you",
                       text="Please confirm your full name, date of birth and gender.",
                       parts=[_p("P_NAME", "Full name", "text", prefill_key="fullName"),
                              _p("P_DOB", "Date of birth", "date", prefill_key="dateOfBirth"),
                              _p("P_GENDER", "Gender", "select", options=["Male", "Female", "Other"], prefill_key="gender")]),
    QuestionDefinition(question_id="Q2", number=2, title="Weight and contact",
                       text="What is your weight in kilograms, and a contact number we can reach you on?",
                       parts=[_p("P_WEIGHT_KG", "Weight (kg)", "number", min=20, max=250),
                              _p("P_CONTACT", "Contact number", "text", prefill_key="phoneNumber")]),
    QuestionDefinition(question_id="Q3", number=3, title="Donation history",
                       text="Have you donated blood before? If yes, when was your last donation?",
                       parts=[_p("DH_BEFORE", "Donated blood before?", "yes_no"),
                              _p("DH_LAST_DATE", "Date of your last donation", "date", allow_unknown=True,
                                 show_if={"field": "DH_BEFORE", "equals": "Yes"})]),
    QuestionDefinition(question_id="Q4", number=4, title="Today's health",
                       text=("Are you feeling well today? Have you had a fever, cold, cough or any infection in the last 2 weeks? "
                             "Have you eaten a main meal in the last 4 hours, slept at least 6 hours, and have you had any alcohol in the last 24 hours?"),
                       parts=[_p("CH_WELL", "Feeling well today?", "yes_no"),
                              _p("CH_INFECTION_2W", "Fever, cold, cough or infection in the last 2 weeks?", "yes_no"),
                              _p("CH_MEAL_4H", "Main meal in the last 4 hours?", "yes_no"),
                              _p("CH_SLEEP_6H", "Slept at least 6 hours?", "yes_no"),
                              _p("CH_ALCOHOL_24H", "Alcohol in the last 24 hours?", "yes_no")]),
    QuestionDefinition(question_id="Q5", number=5, title="Illness and medicines",
                       text=("Do you have any serious or long-term illness: heart disease, diabetes, high blood pressure, epilepsy, "
                             "hepatitis, cancer or a bleeding disorder? Are you taking any medicines now?"),
                       parts=[_p("MH_CONDITIONS", "Serious or long-term illness", "checklist", options=CONDITIONS, allow_none=True),
                              _p("MH_MEDICINES", "Taking any medicines now?", "yes_no"),
                              _p("MH_MEDICINES_DETAILS", "Which medicines?", "text", required=False,
                                 show_if={"field": "MH_MEDICINES", "equals": "Yes"})]),
    QuestionDefinition(question_id="Q6", number=6, title="Tattoos, surgery and travel",
                       text=("In the last 2 years, have you had a tattoo, surgery or a blood transfusion? "
                             "In the last 3 years, have you travelled abroad? If so, which country and when did you come back?"),
                       parts=[_p("RE_EVENTS", "In the last 2 years", "checklist", options=RECENT_EVENTS, allow_none=True),
                              _p("RE_TATTOO_DATE", "Date of the tattoo", "date", allow_unknown=True, show_if={"field": "RE_EVENTS", "includes": "Tattoo"}),
                              _p("RE_SURGERY_DATE", "Date of the surgery", "date", allow_unknown=True, show_if={"field": "RE_EVENTS", "includes": "Surgery"}),
                              _p("RE_TRANSFUSION_DATE", "Date of the blood transfusion", "date", allow_unknown=True,
                                 show_if={"field": "RE_EVENTS", "includes": "Blood transfusion"}),
                              _p("TR_ABROAD", "Travelled abroad in the last 3 years?", "yes_no"),
                              _p("TR_TRIPS", "Country and return date", "trips", show_if={"field": "TR_ABROAD", "equals": "Yes"})]),
    QuestionDefinition(question_id="Q7", number=7, title="Final questions (confidential)", confidential=True,
                       text=("These answers are confidential and seen only by the doctor. "
                             "Do any of the National Blood Transfusion Service's risk behaviours apply to you? For example: ever injecting drugs, "
                             "sex for money or drugs, more than one sexual partner in the last 12 months, or a partner who has HIV or hepatitis. "
                             "Answer yes or no only; no details are recorded."),
                       parts=[_p("FD_STATUS", "Pregnant, breastfeeding, or gave birth in the last year?", "checklist", options=FEMALE_STATUS,
                                 allow_none=True, female_only=True),
                              _p("IR_RISK", "Do any of these risk behaviours apply to you?", "yes_no")]),
]

QUESTIONS_BY_ID = {q.question_id: q for q in QUESTIONS}
PARTS_BY_ID = {p.id: p for q in QUESTIONS for p in q.parts}
CONFIRM_ID = "CONFIRM_TRUE"
CONFIRM_TEXT = "I confirm my answers are true"
NIC_REMINDER = "Please remember to bring your NIC (National Identity Card) when you come to donate."


def is_female(profile: Dict[str, Any], answers: Dict[str, str]) -> bool:
    gender = answers.get("P_GENDER") or profile.get("gender") or ""
    return gender.strip().lower() == "female"


def _list(value: Optional[str]) -> List[str]:
    import json
    if not value:
        return []
    try:
        parsed = json.loads(value)
        return [str(v) for v in parsed] if isinstance(parsed, list) else []
    except (ValueError, TypeError):
        return []


def part_applies(part: PartDefinition, profile: Dict[str, Any], answers: Dict[str, str]) -> bool:
    if part.female_only and not is_female(profile, answers):
        return False
    if not part.show_if:
        return True
    parent = answers.get(part.show_if["field"])
    if "equals" in part.show_if:
        return (parent or "") == part.show_if["equals"]
    return part.show_if.get("includes") in _list(parent)


def applicable_parts(question: QuestionDefinition, profile: Dict[str, Any], answers: Dict[str, str]) -> List[PartDefinition]:
    return [p for p in question.parts if part_applies(p, profile, answers)]


def missing_parts(question: QuestionDefinition, profile: Dict[str, Any], answers: Dict[str, str]) -> List[PartDefinition]:
    """Required parts of a question that still have no answer (follow-up parts appear once their trigger is answered)."""
    return [p for p in applicable_parts(question, profile, answers) if p.required and p.id not in answers]


def render_question(question: QuestionDefinition, profile: Dict[str, Any], answers: Dict[str, str],
                    previous: Optional[Dict[str, str]] = None, follow_up: bool = False) -> Dict[str, Any]:
    """The question as shown in the chat bubble: its text, every part (with defaults) and which parts are still missing."""
    previous = previous or {}
    female = is_female(profile, answers)
    text = question.text
    if question.question_id == "Q7" and female:
        text = "Are you pregnant or breastfeeding, or have you given birth in the last year? And for everyone: " + text[0].lower() + text[1:]
    missing = [p.id for p in missing_parts(question, profile, answers)]
    parts = []
    for p in question.parts:
        if p.female_only and not female:
            continue
        default = answers.get(p.id) or previous.get(p.id) or (profile.get(p.prefill_key) if p.prefill_key else None)
        parts.append({**p.model_dump(exclude={"prefill_key", "female_only"}), "default": default})
    return {"question_id": question.question_id, "number": question.number, "count": len(QUESTIONS), "title": question.title,
            "text": text, "parts": parts, "missing": missing, "follow_up": follow_up, "confidential": question.confidential, "type": "parts"}


def render_confirm() -> Dict[str, Any]:
    return {"question_id": CONFIRM_ID, "number": None, "count": len(QUESTIONS), "title": "Confirm", "type": "confirm",
            "text": "Finally, please tick the box to confirm your answers and send them to the doctor.",
            "parts": [{"id": CONFIRM_ID, "label": CONFIRM_TEXT, "type": "confirm", "required": True}], "missing": [CONFIRM_ID],
            "follow_up": False, "confidential": False}


def questionnaire_schema() -> List[Dict[str, Any]]:
    """The question and part definitions, stored in each report so the edit form shows exactly what was asked."""
    return [{"question_id": q.question_id, "number": q.number, "title": q.title, "text": q.text, "confidential": q.confidential,
             "parts": [p.model_dump(exclude={"prefill_key"}) for p in q.parts]} for q in QUESTIONS]
