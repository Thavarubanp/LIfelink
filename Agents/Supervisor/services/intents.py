"""Chat intent routing: which Supervisor capabilities a message needs (medical / platform / account)."""
import re
from typing import List

from services import llm

INTENTS = ("medical", "platform", "account")

_MEDICAL = re.compile(
    r"\b(blood (group|type)s?|compatib\w*|universal|o[+-]|ab[+-]|a[+-]|b[+-]|rh|haemoglobin|hemoglobin|"
    r"iron|weigh\w*|age|old|tattoo\w*|piercing|acupuncture|pregnan\w*|breastfeed\w*|abortion|miscarriage|malaria|dengue|"
    r"hepatitis|hiv|syphilis|tuberculosis|tb|g6pd|polycyth\w*|thalass\w*|epilep\w*|seizure\w*|antibiotic\w*|medicine\w*|"
    r"medication\w*|aspirin|warfarin|steroid\w*|vaccin\w*|surgery|transfusion|faint\w*|dizz\w*|side effects?|"
    r"after (giving|donating)|recover\w*|stor(e|ed|age|ing)|shelf life|how long|expir\w*|cold chain|plasma|platelet\w*|"
    r"deferr\w*|120 days|interval|emergency (blood|release)|o negative|what does .* mean|safe|healthy|sick|ill|fever|"
    r"alcohol|sleep|eat)\b", re.IGNORECASE)

# Generic words that mean "medical" only when the message is not about using LifeLink
_MEDICAL_WEAK = re.compile(r"\b(donat\w*|eligib\w*|blood)\b", re.IGNORECASE)

_PLATFORM = re.compile(
    r"\b(how (do|can) i|how to|where (do|can|is)|lifelink|page|button|create (a )?(blood )?request|accept|withdraw|delete|"
    r"transfer|inventory|packet|threshold|record (a )?donation|release|verify|assign|screening (queue|report)|notification\w*|"
    r"profile|complaint|appeal|suspend\w*|emergency (center|hub)|critical request|dashboard|sign in|password)\b", re.IGNORECASE)

_ACCOUNT = re.compile(
    r"\b(my|mine|me|our|status|next|pending|waiting|todo|to do|attention|priorit\w*|what should i|summary|overview|"
    r"when can i|am i eligible|can i donate (again|now)|shortage|low stock|expiring|queue)\b", re.IGNORECASE)

_GREETING = re.compile(r"^\s*(hi|hello|hey|good (morning|afternoon|evening)|thanks|thank you|ok|okay)[\s!.]*$", re.IGNORECASE)


def rule_intents(message: str) -> List[str]:
    intents: List[str] = []
    platform = bool(_PLATFORM.search(message))
    if _ACCOUNT.search(message):
        intents.append("account")
    if platform:
        intents.append("platform")
    if _MEDICAL.search(message) or (not platform and _MEDICAL_WEAK.search(message)):
        intents.append("medical")
    return intents


async def classify(message: str, role: str) -> List[str]:
    """Rules first (fast, deterministic); Gemini refines when configured. Greetings need no retrieval."""
    if _GREETING.match(message):
        return []
    intents = rule_intents(message)
    if llm.llm_available():
        result = await llm.complete_json(
            "Classify a LifeLink blood donation platform chat message. Return JSON {\"intents\": [...]} using only "
            "\"medical\" (blood donation/medical guidance), \"platform\" (how LifeLink works) and \"account\" "
            "(the user's own requests, donations, queue or stock). Use every intent that applies.",
            f"User role: {role}\nMessage: {message}")
        if isinstance(result, dict) and isinstance(result.get("intents"), list):
            refined = [i for i in result["intents"] if i in INTENTS]
            if refined:
                intents = list(dict.fromkeys(refined))
    return intents or ["platform"]
