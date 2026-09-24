"""
Deterministic screening rules. They raise flags for the doctor and set the AI recommendation, but they never
approve or reject: the reviewing doctor decides.

severity: "defer"  -> temporary deferral indicated (e.g. recent tattoo, feeling unwell today)
          "review" -> needs the doctor's judgement
          "high"   -> serious risk for the donor or the patient
"""
import json
from datetime import date
from typing import Dict, List, Optional

HIGH_RISK_CONDITIONS = {"Heart disease", "Heart surgery", "Stroke", "Chronic lung disease", "Kidney disease", "Liver disease",
                        "Cancer", "Blood disorders", "Bleeding disorders", "Polycythemia", "Leprosy"}
HIGH_RISK_DISEASES = {"Hepatitis B", "Hepatitis C"}
DEFER_EVENTS = {"Blood transfusion", "Surgery", "Hospitalization", "Serious accident", "Rabies treatment", "Acupuncture",
                "Tattoo", "Piercing", "Imprisonment"}
DEFER_MEDICATION = {"Antibiotics", "Medication for an infection", "Dental procedure"}


def _items(value: Optional[str]) -> List[str]:
    if not value:
        return []
    try:
        parsed = json.loads(value)
        return [str(v) for v in parsed] if isinstance(parsed, list) else []
    except json.JSONDecodeError:
        return []


def _age(dob: Optional[str]) -> Optional[int]:
    if not dob:
        return None
    try:
        born = date.fromisoformat(dob[:10])
    except ValueError:
        return None
    today = date.today()
    return today.year - born.year - ((today.month, today.day) < (born.month, born.day))


def _days_since(value: Optional[str]) -> Optional[int]:
    if not value:
        return None
    try:
        when = date.fromisoformat(value if len(value) == 10 else f"{value}-01")
    except ValueError:
        return None
    return (date.today() - when).days


def evaluate(answers: Dict[str, str]) -> Dict:
    flags: List[Dict[str, str]] = []

    def flag(code: str, severity: str, section: int, message: str):
        flags.append({"code": code, "severity": severity, "section": section, "message": message})

    yes = lambda qid: (answers.get(qid) or "").lower() == "yes"  # noqa: E731
    no = lambda qid: (answers.get(qid) or "").lower() == "no"  # noqa: E731

    age = _age(answers.get("P_DOB"))
    if age is not None and not 18 <= age <= 60:
        flag("AGE_OUT_OF_RANGE", "defer", 4, f"Age {age} is outside 18-60.")
    if no("BE_AGE_18_60"):
        flag("AGE_OUT_OF_RANGE_DECLARED", "defer", 4, "Donor states they are not between 18 and 60.")
    if no("BE_WEIGHT_50"):
        flag("UNDER_WEIGHT", "defer", 4, "Donor weighs 50 kg or less.")

    since_last = _days_since(answers.get("DH_LAST_DATE"))
    if since_last is not None and since_last < 120:
        flag("DONATION_INTERVAL", "defer", 2, f"Last donation was {since_last} days ago (minimum 120).")
    if no("DH_120_DAYS"):
        flag("DONATION_INTERVAL_DECLARED", "defer", 2, "Donor states 120 days have not passed since the last donation.")
    if yes("DH_ADVISED_NOT"):
        flag("ADVISED_NOT_TO_DONATE", "review", 2, "Donor was previously advised not to donate.")
    complications = _items(answers.get("DH_COMPLICATIONS"))
    if complications:
        flag("PREVIOUS_COMPLICATIONS", "review", 2, "Previous donation complications: " + ", ".join(complications) + ".")

    if no("CH_WELL"):
        flag("UNWELL_TODAY", "defer", 3, "Donor is not feeling well today.")
    if no("CH_ATE_4H"):
        flag("NOT_EATEN", "review", 3, "No meal in the last 4 hours; advise eating before donation.")
    if no("CH_SLEPT_6H"):
        flag("INSUFFICIENT_SLEEP", "review", 3, "Less than 6 hours of sleep.")
    symptoms = _items(answers.get("CH_SYMPTOMS"))
    if symptoms:
        flag("CURRENT_SYMPTOMS", "defer", 3, "Current symptoms: " + ", ".join(symptoms) + ".")
    if yes("CH_TREATMENT"):
        flag("UNDER_TREATMENT", "review", 3, "Currently under medical treatment.")
    if yes("CH_ALCOHOL_24H"):
        flag("RECENT_ALCOHOL", "defer", 3, "Alcohol in the last 24 hours.")

    for qid, label in (("BE_PREGNANT", "Pregnant"), ("BE_BREASTFEEDING", "Breastfeeding"), ("BE_ABORTION_6M", "Abortion within 6 months"),
                       ("FD_DELIVERED_1Y", "Gave birth within 12 months"), ("FD_MISCARRIAGE", "Miscarriage within 6 months")):
        if yes(qid):
            flag(f"FEMALE_{qid}", "defer", 12, f"{label}.")

    conditions = _items(answers.get("MH_CONDITIONS"))
    high = [c for c in conditions if c in HIGH_RISK_CONDITIONS]
    other = [c for c in conditions if c not in HIGH_RISK_CONDITIONS]
    if high:
        flag("SERIOUS_MEDICAL_HISTORY", "high", 5, "Medical history: " + ", ".join(high) + ".")
    if other:
        flag("MEDICAL_HISTORY", "review", 5, "Medical history: " + ", ".join(other) + ".")

    events = _items(answers.get("RE_EVENTS"))
    deferring = [e for e in events if e in DEFER_EVENTS]
    if deferring:
        flag("RECENT_EVENTS", "defer", 6, "Within 12 months: " + ", ".join(deferring) + ".")
    if "Vaccination" in events:
        flag("RECENT_VACCINATION", "review", 6, "Vaccination within 12 months; check the vaccine type and date.")

    diseases = _items(answers.get("RD_DISEASES"))
    serious = [d for d in diseases if d in HIGH_RISK_DISEASES]
    temporary = [d for d in diseases if d not in HIGH_RISK_DISEASES]
    if serious:
        flag("HEPATITIS_HISTORY", "high", 7, "Within 12 months: " + ", ".join(serious) + ".")
    if temporary:
        flag("RECENT_ILLNESS", "defer", 7, "Within 12 months: " + ", ".join(temporary) + ".")
    if yes("RD_HEPATITIS_CONTACT"):
        flag("HEPATITIS_CONTACT", "defer", 7, "Close contact with hepatitis or jaundice within 12 months.")
    if yes("RD_ANTIMALARIAL_3Y"):
        flag("ANTIMALARIAL", "review", 7, "Anti-malarial medication in the last 3 years.")

    medication = _items(answers.get("DM_ITEMS"))
    if any(m in DEFER_MEDICATION for m in medication):
        flag("RECENT_MEDICATION_OR_DENTAL", "defer", 8, "Recent: " + ", ".join(m for m in medication if m in DEFER_MEDICATION) + ".")
    if "Blood thinners" in medication:
        flag("BLOOD_THINNERS", "review", 8, "Takes blood thinners.")
    if "Steroids" in medication or "Prescription medication" in medication:
        flag("MEDICATION_REVIEW", "review", 8, "Prescription medication or steroids; check the medication list.")

    if yes("TR_MALARIA"):
        flag("MALARIA_TRAVEL", "defer", 9, "Travel to a malaria-endemic country.")
    elif yes("TR_ABROAD"):
        flag("TRAVEL", "review", 9, "Travel outside Sri Lanka in the past 3 years.")

    if _items(answers.get("IR_ITEMS")):
        # Confidential: the flag names the section, not the answers
        flag("INFECTION_RISK_DISCLOSED", "high", 10, "Infectious disease risk disclosed in the confidential section.")

    recent_symptoms = _items(answers.get("RS_SYMPTOMS"))
    if recent_symptoms:
        flag("RECENT_SYMPTOMS", "high" if len(recent_symptoms) >= 2 else "review", 11,
             "Past 6 months: " + ", ".join(recent_symptoms) + ".")

    if no("CONSENT"):
        flag("NO_CONSENT", "review", 12, "Donor did not confirm the declaration.")

    severities = {f["severity"] for f in flags}
    if "high" in severities:
        risk, recommendation = "HIGH", "Requires Doctor Review"
    elif "defer" in severities:
        risk, recommendation = "MEDIUM", "Temporarily Deferred"
    elif "review" in severities:
        risk, recommendation = "MEDIUM", "Requires Doctor Review"
    else:
        risk, recommendation = "LOW", "Eligible"
    return {"risk_level": risk, "recommendation": recommendation, "flags": flags, "age": age}
