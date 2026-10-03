"""
Turns the donor's input into field values for the current question.

- Structured input (the inputs inside the chat bubble, or the edit form) is validated part by part.
- Free text is parsed with fixed rules: confirming pre-filled values ("yes, that's right"), a single missing part, a run
  of yes/no answers, ticked items or "none". Anything else is handed to the LLM extractor by the screening service
  (never for the confidential question 7), and if that is not possible the agent asks for one part at a time.
- Questions ("what is ...?") and withdrawal requests are recognised and never recorded as answers.
"""
import json
import re
from dataclasses import dataclass, field
from datetime import date, datetime
from typing import Any, Dict, List, Optional, Tuple

from models.donor_screening import DONT_REMEMBER, PartDefinition, QuestionDefinition

YES = {"yes", "y", "yeah", "yep", "yes i have", "yes i did", "i have", "i did", "true", "correct", "confirm", "confirmed", "ok", "okay", "sure", "right", "that's right", "thats right"}
NO = {"no", "n", "nope", "never", "not", "false", "no i have not", "no i haven't", "i have not", "i haven't", "i did not", "i didn't"}
NONE_WORDS = {"none", "no", "nothing", "none of these", "none of them", "n/a", "na", "nil", "-", "[]"}
UNKNOWN = re.compile(r"\b(don'?t|do not|can'?t|cannot) (remember|recall|know)\b|\bforgot\b|\bnot sure\b|\bunknown\b|\bno idea\b|"
                     + re.escape(DONT_REMEMBER.lower()), re.IGNORECASE)
QUESTION_START = re.compile(r"^(what|why|how|who|which|when does|does|do i|is it|is a|is an|are|can|could|should|explain|meaning|tell me|define)\b", re.IGNORECASE)
WITHDRAW = re.compile(r"\b(withdraw|cancel (my|this) (donation|acceptance)|stop (the )?(screening|donation)|don'?t want to donate|no longer want to donate)\b", re.IGNORECASE)
CONFIRM_WORDS = re.compile(r"^(yes|yeah|yep|ok|okay|sure|correct|confirm(ed)?|that'?s (right|correct)|all correct|right)\b", re.IGNORECASE)


@dataclass
class ParsedMessage:
    kind: str  # "answer", "question", "clarify", "withdraw", "extract"
    values: Dict[str, str] = field(default_factory=dict)
    query: Optional[str] = None
    hint: Optional[str] = None


def _clean(text: str) -> str:
    return re.sub(r"\s+", " ", (text or "").strip().strip(".!").lower())


def is_question(message: str) -> bool:
    text = (message or "").strip()
    return text.endswith("?") or bool(QUESTION_START.match(text))


def parse_yes_no(text: str) -> Optional[str]:
    cleaned = _clean(text)
    first_word = re.split(r"[^a-z']", cleaned)[0] if cleaned else ""
    if cleaned in YES or first_word in ("yes", "yeah", "yep", "y"):
        return "Yes"
    if cleaned in NO or first_word in ("no", "nope", "never", "n"):
        return "No"
    return None


def parse_date(text: str) -> Optional[str]:
    cleaned = (text or "").strip()
    for fmt in ("%Y-%m-%d", "%d/%m/%Y", "%d-%m-%Y", "%d.%m.%Y", "%Y/%m/%d", "%d %B %Y", "%d %b %Y", "%B %d %Y", "%b %d %Y", "%B %d, %Y"):
        try:
            return datetime.strptime(cleaned, fmt).date().isoformat()
        except ValueError:
            continue
    for fmt in ("%Y-%m", "%m/%Y", "%B %Y", "%b %Y"):
        try:
            return datetime.strptime(cleaned, fmt).strftime("%Y-%m-01")
        except ValueError:
            continue
    match = re.search(r"\b(\d{4}-\d{2}-\d{2})\b", cleaned)
    return match.group(1) if match else None


def parse_number(text: str) -> Optional[float]:
    match = re.search(r"\d+(?:\.\d+)?", text or "")
    return float(match.group(0)) if match else None


def _fmt_number(value: float) -> str:
    return str(int(value)) if float(value).is_integer() else f"{value:.1f}"


def parse_checklist(value: Any, options: List[str]) -> Optional[List[str]]:
    """Ticked options, [] for "none of these", or None when nothing could be understood."""
    if isinstance(value, list):
        if not value or all(str(v).strip().lower() in NONE_WORDS for v in value):
            return []
        picked = [o for o in options if any(str(v).strip().lower() == o.lower() for v in value)]
        return picked if len(picked) == len([v for v in value if str(v).strip()]) else None
    text = str(value or "").strip()
    if text.startswith("["):
        try:
            return parse_checklist(json.loads(text), options)
        except json.JSONDecodeError:
            pass
    cleaned = _clean(text)
    if cleaned in NONE_WORDS or cleaned.startswith("none") or cleaned in ("no, none", "no none"):
        return []
    picked = [o for o in options if o.lower() in cleaned]
    return picked or None


def parse_trips(value: Any) -> Optional[List[Dict[str, str]]]:
    """[{country, return_date}] from the trip inputs; None when a trip has no country or a bad date."""
    if isinstance(value, str):
        try:
            value = json.loads(value)
        except json.JSONDecodeError:
            return None
    if not isinstance(value, list) or not value:
        return None
    trips = []
    for trip in value:
        if not isinstance(trip, dict):
            return None
        country = str(trip.get("country") or "").strip()[:80]
        raw_date = str(trip.get("return_date") or "").strip()
        if not country:
            return None
        if UNKNOWN.search(raw_date) or raw_date == DONT_REMEMBER:
            returned = DONT_REMEMBER
        else:
            returned = parse_date(raw_date)
            if not returned or returned > date.today().isoformat():
                return None
        trips.append({"country": country, "return_date": returned})
    return trips


def parse_part(part: PartDefinition, value: Any) -> Tuple[Optional[str], Optional[str]]:
    """(stored value, error) for one part's input."""
    if value is None or (isinstance(value, str) and not value.strip()):
        return None, f"{part.label} is needed."
    if part.type in ("date",) and part.allow_unknown and (str(value) == DONT_REMEMBER or UNKNOWN.search(str(value))):
        return DONT_REMEMBER, None
    if part.type == "text":
        return str(value).strip()[:200], None
    if part.type == "number":
        number = value if isinstance(value, (int, float)) else parse_number(str(value))
        if number is None:
            return None, f"{part.label}: please enter a number."
        if (part.min is not None and number < part.min) or (part.max is not None and number > part.max):
            return None, f"{part.label}: please check the value ({_fmt_number(number)})."
        return _fmt_number(float(number)), None
    if part.type == "date":
        parsed = parse_date(str(value))
        if not parsed:
            return None, f"{part.label}: please enter a date (YYYY-MM-DD)."
        if parsed > date.today().isoformat():
            return None, f"{part.label}: the date cannot be in the future."
        return parsed, None
    if part.type == "select":
        text = _clean(str(value)).replace(" ", "")
        match = next((o for o in part.options or [] if o.lower().replace(" ", "") == text), None)
        return (match, None) if match else (None, f"{part.label}: choose one of {', '.join(part.options or [])}.")
    if part.type == "yes_no":
        answer = value if value in ("Yes", "No") else parse_yes_no(str(value))
        return (answer, None) if answer else (None, f"{part.label}: please answer yes or no.")
    if part.type == "checklist":
        picked = parse_checklist(value, part.options or [])
        return (json.dumps(picked), None) if picked is not None else (None, f"{part.label}: tick any that apply, or None of these.")
    if part.type == "trips":
        trips = parse_trips(value)
        return (json.dumps(trips), None) if trips else (None, "Please give each country and the date you came back.")
    return str(value)[:200], None


def parse_structured(question: QuestionDefinition, values: Dict[str, Any]) -> Tuple[Dict[str, str], List[str]]:
    """Valid values and error messages for the parts of `question` present in `values`."""
    parsed, errors = {}, []
    for part in question.parts:
        if part.id not in values:
            continue
        value, error = parse_part(part, values[part.id])
        if error and not part.required and (values[part.id] in (None, "")):
            continue
        if error:
            errors.append(error)
        else:
            parsed[part.id] = value
    return parsed, errors


def parse_text(message: str, missing: List[PartDefinition], defaults: Dict[str, Any]) -> ParsedMessage:
    """Rule-based reading of a free-text reply. kind "extract" means the rules could not read it (LLM next, if allowed)."""
    text = (message or "").strip()
    if not text:
        return ParsedMessage("clarify", hint="Please answer using the options above, or type your answer.")
    if WITHDRAW.search(text):
        return ParsedMessage("withdraw")
    if is_question(text) and parse_yes_no(text) is None:
        return ParsedMessage("question", query=text)

    # "Yes, that's right": keep the pre-filled values (Section 1 details from the profile, or the previous answers)
    if CONFIRM_WORDS.match(text) and missing and all(defaults.get(p.id) not in (None, "") for p in missing):
        values = {}
        for p in missing:
            value, error = parse_part(p, defaults[p.id])
            if error:
                break
            values[p.id] = value
        else:
            return ParsedMessage("answer", values=values)

    if len(missing) == 1:
        value, error = parse_part(missing[0], text)
        if not error:
            return ParsedMessage("answer", values={missing[0].id: value})
        return ParsedMessage("clarify", hint=error)

    # A run of yes/no answers for yes/no parts, in order ("yes, no, yes, yes, no")
    if missing and all(p.type == "yes_no" for p in missing):
        words = [parse_yes_no(w) for w in re.split(r"[,;/\n]+|\s+", text) if parse_yes_no(w)]
        if len(words) == len(missing):
            return ParsedMessage("answer", values={p.id: w for p, w in zip(missing, words)})

    # Everything "none / no": tick lists are empty and yes/no parts are No
    if _clean(text) in ("none", "no", "none of these", "no to all", "none of them", "nothing", "no, none"):
        values = {}
        for p in missing:
            if p.type == "checklist" and p.allow_none:
                values[p.id] = "[]"
            elif p.type == "yes_no":
                values[p.id] = "No"
        if values and len(values) == len(missing):
            return ParsedMessage("answer", values=values)

    return ParsedMessage("extract")


def display_value(part: Optional[PartDefinition], value: Optional[str]) -> str:
    if value is None:
        return ""
    if part is not None and part.type == "checklist":
        try:
            items = json.loads(value)
            return ", ".join(items) if items else "None of these"
        except json.JSONDecodeError:
            return value
    if part is not None and part.type == "trips":
        try:
            return "; ".join(f"{t['country']} (returned {t['return_date']})" for t in json.loads(value))
        except (json.JSONDecodeError, KeyError, TypeError):
            return value
    if part is not None and part.type == "number" and part.id == "P_WEIGHT_KG":
        return f"{value} kg"
    return value
