"""
Turns a donor's chat message into a structured answer for the current question, or recognises that the donor
is asking a question or wants to withdraw. Parsing is rule-based; the confidential section never leaves this module.
"""
import json
import re
from dataclasses import dataclass
from datetime import date, datetime
from typing import Dict, List, Optional

from models.donor_screening import QuestionDefinition

YES = {"yes", "y", "yeah", "yep", "yes i have", "yes i did", "i have", "i did", "true", "correct", "confirm", "confirmed", "ok", "okay", "sure"}
NO = {"no", "n", "nope", "never", "not", "false", "no i have not", "no i haven't", "i have not", "i haven't", "i did not", "i didn't"}
NONE_WORDS = {"none", "no", "nothing", "none of these", "none of them", "n/a", "na", "nil", "-"}
SAME_WORDS = {"same", "keep", "keep it", "same as before", "unchanged"}
QUESTION_START = re.compile(r"^(what|why|how|who|which|when does|does|do i|is it|is a|is an|are|can|could|should|explain|meaning|tell me|define)\b", re.IGNORECASE)
WITHDRAW = re.compile(r"\b(withdraw|cancel (my|this) (donation|acceptance)|stop (the )?(screening|donation)|don'?t want to donate|no longer want to donate)\b", re.IGNORECASE)
NUMBER_WORDS = {"once": 1, "one": 1, "twice": 2, "two": 2, "three": 3, "four": 4, "five": 5, "six": 6, "seven": 7, "eight": 8, "nine": 9, "ten": 10}
BLOOD_GROUP_WORDS = {"positive": "+", "pos": "+", "negative": "-", "neg": "-"}


@dataclass
class ParsedMessage:
    kind: str  # "answer", "question", "clarify", "withdraw"
    value: Optional[str] = None
    query: Optional[str] = None
    hint: Optional[str] = None


def _clean(text: str) -> str:
    return re.sub(r"\s+", " ", text.strip().strip(".!").lower())


def is_question(message: str) -> bool:
    text = message.strip()
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
    cleaned = text.strip()
    for fmt in ("%Y-%m-%d", "%d/%m/%Y", "%d-%m-%Y", "%d.%m.%Y", "%Y/%m/%d", "%d %B %Y", "%d %b %Y", "%B %d %Y", "%b %d %Y"):
        try:
            return datetime.strptime(cleaned, fmt).date().isoformat()
        except ValueError:
            continue
    for fmt in ("%Y-%m", "%m/%Y", "%B %Y", "%b %Y"):
        try:
            return datetime.strptime(cleaned, fmt).strftime("%Y-%m")
        except ValueError:
            continue
    return None


def parse_number(text: str) -> Optional[str]:
    match = re.search(r"\d+", text)
    if match:
        return match.group(0)
    for word, value in NUMBER_WORDS.items():
        if re.search(rf"\b{word}\b", text.lower()):
            return str(value)
    return None


def parse_select(text: str, options: List[str]) -> Optional[str]:
    cleaned = _clean(text)
    for word, symbol in BLOOD_GROUP_WORDS.items():
        cleaned = re.sub(rf"\s*\b{word}\b", symbol, cleaned)
    cleaned = cleaned.replace(" ", "")
    for option in options:
        if cleaned == option.lower().replace(" ", ""):
            return option
    if cleaned in ("dontknow", "don'tknow", "notsure", "unknown", "idk"):
        return next((o for o in options if o.lower().startswith("don")), None)
    return None


def parse_checklist(text: str, options: List[str]) -> Optional[List[str]]:
    """Returns the ticked options, [] for none, or None when nothing could be understood."""
    stripped = text.strip()
    if stripped.startswith("["):
        try:
            values = json.loads(stripped)
            picked = [o for o in options if any(str(v).strip().lower() == o.lower() for v in values)]
            if picked or values == []:
                return picked
        except json.JSONDecodeError:
            pass
    cleaned = _clean(text)
    if cleaned in NONE_WORDS:
        return []
    picked: List[str] = []
    for part in re.split(r",|;|\band\b|\n", cleaned):
        part = part.strip()
        if not part:
            continue
        if part.isdigit() and 1 <= int(part) <= len(options):
            picked.append(options[int(part) - 1])
            continue
        for option in options:
            label = option.lower()
            if label in part or part in label and len(part) >= 3:
                picked.append(option)
    return list(dict.fromkeys(picked)) or None


def parse(message: str, question: QuestionDefinition, profile: Dict, previous: Optional[str] = None) -> ParsedMessage:
    text = (message or "").strip()
    if not text:
        return ParsedMessage("clarify", hint="Please type your answer.")
    if WITHDRAW.search(text):
        return ParsedMessage("withdraw")
    if previous and _clean(text) in SAME_WORDS:
        return ParsedMessage("answer", value=previous)
    # A question about the question is never recorded as an answer
    if is_question(text):
        return ParsedMessage("question", query=text)

    qtype = question.question_type
    prefill = profile.get(question.prefill_key) if question.prefill_key else None

    if qtype == "confirm":
        if prefill and parse_yes_no(text) == "Yes":
            return ParsedMessage("answer", value=str(prefill))
        if is_question(text):
            return ParsedMessage("question", query=text)
        return ParsedMessage("answer", value=text[:300])

    if qtype in ("date", "select") and prefill and parse_yes_no(text) == "Yes":
        return ParsedMessage("answer", value=str(prefill)[:10] if qtype == "date" else str(prefill))

    if qtype == "yes_no":
        value = parse_yes_no(text)
        if value:
            return ParsedMessage("answer", value=value)
        if is_question(text) or len(text.split()) > 3:
            return ParsedMessage("question", query=text)
        return ParsedMessage("clarify", hint="Please answer yes or no, or ask me what the question means.")

    if qtype == "date":
        value = parse_date(text)
        if value:
            if value > date.today().isoformat():
                return ParsedMessage("clarify", hint="That date is in the future. Please enter a past date (YYYY-MM-DD).")
            return ParsedMessage("answer", value=value)
        if is_question(text):
            return ParsedMessage("question", query=text)
        return ParsedMessage("clarify", hint="Please enter the date as YYYY-MM-DD (or YYYY-MM if you only know the month).")

    if qtype == "number":
        value = parse_number(text)
        if value:
            return ParsedMessage("answer", value=value)
        if is_question(text):
            return ParsedMessage("question", query=text)
        return ParsedMessage("clarify", hint="Please enter a number.")

    if qtype == "select":
        value = parse_select(text, question.options or [])
        if value:
            return ParsedMessage("answer", value=value)
        if is_question(text):
            return ParsedMessage("question", query=text)
        return ParsedMessage("clarify", hint="Please choose one of: " + ", ".join(question.options or []) + ".")

    if qtype == "checklist":
        picked = parse_checklist(text, question.options or [])
        if picked is not None:
            return ParsedMessage("answer", value=json.dumps(picked))
        if is_question(text):
            return ParsedMessage("question", query=text)
        return ParsedMessage("clarify", hint="Please choose any that apply (for example: " + ", ".join((question.options or [])[:2]) + "), or reply None.")

    # text
    if is_question(text) and len(text) < 200:
        return ParsedMessage("question", query=text)
    return ParsedMessage("answer", value=text[:1000])


def display_answer(question: QuestionDefinition, value: Optional[str]) -> str:
    if value is None:
        return ""
    if question.question_type == "checklist":
        try:
            items = json.loads(value)
            return ", ".join(items) if items else "None"
        except json.JSONDecodeError:
            return value
    return value
