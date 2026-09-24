"""
Output guardrails for chat replies.

1. Data scope: emails, phone numbers and IDs that do not appear in the user's own snapshot are removed, so a reply
   can never expose another person's details even if a model invents or repeats them.
2. AI authority: the assistant never claims to approve, reject or verify; medical guidance always says the
   doctor makes the final decision.
"""
import json
import re
from typing import Any, Dict

EMAIL = re.compile(r"[\w.+-]+@[\w-]+(?:\.[\w-]+)+")
PHONE = re.compile(r"\+?\d[\d\s-]{7,}\d")
GUID = re.compile(r"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", re.IGNORECASE)

AUTHORITY_CLAIMS = [
    (re.compile(r"\bI (have )?(approved|rejected|verified|cleared)\b", re.IGNORECASE), "the doctor decides"),
    (re.compile(r"\byou (are|have been) (approved|cleared) (to donate|for donation)\b", re.IGNORECASE),
     "the doctor will decide whether you can donate"),
]

MEDICAL_DISCLAIMER = "The doctor at the blood bank makes the final decision about eligibility."


def _allowed_text(snapshot: Dict[str, Any], extra: str = "") -> str:
    return (json.dumps(snapshot, default=str) + " " + extra).lower()


def redact(text: str, snapshot: Dict[str, Any], extra_allowed: str = "") -> str:
    allowed = _allowed_text(snapshot, extra_allowed)

    def keep_or_hide(match: re.Match) -> str:
        value = match.group(0)
        return value if value.lower() in allowed else "[hidden]"

    def phone(match: re.Match) -> str:
        value = match.group(0)
        if sum(ch.isdigit() for ch in value) < 9:  # dates, counts and ranges are not phone numbers
            return value
        return keep_or_hide(match)

    text = EMAIL.sub(keep_or_hide, text)
    text = GUID.sub(keep_or_hide, text)
    return PHONE.sub(phone, text)


def enforce_authority(text: str) -> str:
    for pattern, replacement in AUTHORITY_CLAIMS:
        text = pattern.sub(replacement, text)
    return text


def with_medical_disclaimer(text: str) -> str:
    return text if MEDICAL_DISCLAIMER.lower() in text.lower() else f"{text.rstrip()}\n\n{MEDICAL_DISCLAIMER}"
