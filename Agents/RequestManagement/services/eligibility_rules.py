"""
Deterministic screening rules for the 7-question NBTS questionnaire. They only FLAG answers for the reviewing doctor:
they never approve or reject, and no LLM is involved.

severity: "defer"  -> likely temporary deferral; the doctor confirms (age, first-time donor over 55, weight, donation
                      interval, recent tattoo, malaria-risk or recent foreign travel, pregnancy/breastfeeding/birth,
                      risk behaviour)
          "review" -> needs the doctor's judgement (illness, medicines, surgery, transfusion, unwell, infection, no meal,
                      too little sleep, alcohol, a date the donor does not remember)
          "info"   -> for the doctor's information only (the donor does not remember the last donation date)
Haemoglobin (above 12.5 g/dL) is checked by the doctor at the blood bank; it is not asked in the questionnaire.
"""
import json
from datetime import date
from typing import Dict, List, Optional

from models.donor_screening import DONT_REMEMBER

DONATION_INTERVAL_DAYS = 120
TATTOO_DEFERRAL_DAYS = 730          # 2 years
MALARIA_TRAVEL_DAYS = 3 * 365       # 3 years
OTHER_TRAVEL_DAYS = 90              # 3 months

# Countries with ongoing malaria transmission.
# Source: WHO, World Malaria Report 2023 (countries and territories with indigenous malaria cases in 2022), with the
# common English names donors type. Countries WHO has since certified malaria-free (Sri Lanka 2016, Belize 2023,
# Cabo Verde 2024) are not listed.
# The NBTS deferral list should be checked against this list before production use (see "Decisions to review").
MALARIA_RISK_COUNTRIES = {
    # Africa
    "angola", "benin", "botswana", "burkina faso", "burundi", "cameroon", "central african republic",
    "chad", "comoros", "congo", "republic of the congo", "democratic republic of the congo", "dr congo", "drc", "ivory coast",
    "cote d'ivoire", "côte d'ivoire", "djibouti", "equatorial guinea", "eritrea", "eswatini", "swaziland", "ethiopia", "gabon",
    "gambia", "the gambia", "ghana", "guinea", "guinea-bissau", "kenya", "liberia", "madagascar", "malawi", "mali", "mauritania",
    "mozambique", "namibia", "niger", "nigeria", "rwanda", "sao tome and principe", "senegal", "sierra leone", "somalia",
    "south africa", "south sudan", "sudan", "tanzania", "togo", "uganda", "zambia", "zimbabwe",
    # Eastern Mediterranean
    "afghanistan", "iran", "pakistan", "saudi arabia", "yemen",
    # South-East Asia and Western Pacific
    "bangladesh", "bhutan", "india", "indonesia", "myanmar", "burma", "nepal", "thailand", "timor-leste", "east timor",
    "cambodia", "laos", "lao pdr", "malaysia", "papua new guinea", "philippines", "solomon islands", "south korea",
    "republic of korea", "korea", "north korea", "vanuatu", "vietnam", "viet nam",
    # Americas
    "bolivia", "brazil", "colombia", "costa rica", "dominican republic", "ecuador", "french guiana", "guatemala",
    "guyana", "haiti", "honduras", "mexico", "nicaragua", "panama", "peru", "suriname", "venezuela",
}


def is_malaria_risk_country(country: str) -> bool:
    return (country or "").strip().lower() in MALARIA_RISK_COUNTRIES


def _list(value: Optional[str]) -> List:
    if not value:
        return []
    try:
        parsed = json.loads(value)
        return parsed if isinstance(parsed, list) else []
    except (json.JSONDecodeError, TypeError):
        return []


def _age(dob: Optional[str], today: date) -> Optional[int]:
    try:
        born = date.fromisoformat(str(dob)[:10])
    except (TypeError, ValueError):
        return None
    return today.year - born.year - ((today.month, today.day) < (born.month, born.day))


def _days_since(value: Optional[str], today: date) -> Optional[int]:
    if not value or value == DONT_REMEMBER:
        return None
    try:
        return (today - date.fromisoformat(str(value)[:10])).days
    except ValueError:
        return None


def evaluate(answers: Dict[str, str], today: Optional[date] = None) -> Dict:
    today = today or date.today()
    flags: List[Dict] = []

    def flag(code: str, severity: str, question: int, message: str):
        flags.append({"code": code, "severity": severity, "section": question, "message": message})

    # Q1 - age 18 to 60
    age = _age(answers.get("P_DOB"), today)
    if age is not None and not 18 <= age <= 60:
        flag("AGE_RANGE", "defer", 1, f"Age {age} is outside the donor age range of 18 to 60.")

    # Q2 - weight above 50 kg
    try:
        weight = float(answers.get("P_WEIGHT_KG") or 0)
    except ValueError:
        weight = 0
    if weight and weight <= 50:
        flag("WEIGHT", "defer", 2, f"Weight {answers.get('P_WEIGHT_KG')} kg (50 kg or less).")

    # Q3 - donation history: first-time donors over 55; 120 days since the last donation
    if answers.get("DH_BEFORE") == "No" and age is not None and age > 55:
        flag("FIRST_TIME_OVER_55", "defer", 3, f"First-time donor aged {age} (over 55).")
    if answers.get("DH_BEFORE") == "Yes":
        if answers.get("DH_LAST_DATE") == DONT_REMEMBER:
            flag("LAST_DONATION_UNKNOWN", "info", 3, "Donor doesn't remember the last donation date; check the donation record.")
        else:
            days = _days_since(answers.get("DH_LAST_DATE"), today)
            if days is not None and days < DONATION_INTERVAL_DAYS:
                flag("DONATION_INTERVAL", "defer", 3, f"Last donation {days} day(s) ago (less than {DONATION_INTERVAL_DAYS} days, about 4 months).")

    # Q4 - today's health
    if answers.get("CH_WELL") == "No":
        flag("UNWELL_TODAY", "review", 4, "Donor is not feeling well today.")
    if answers.get("CH_INFECTION_2W") == "Yes":
        flag("RECENT_INFECTION", "review", 4, "Fever, cold, cough or infection in the last 2 weeks.")
    if answers.get("CH_MEAL_4H") == "No":
        flag("NO_MEAL", "review", 4, "No main meal in the last 4 hours.")
    if answers.get("CH_SLEEP_6H") == "No":
        flag("LITTLE_SLEEP", "review", 4, "Less than 6 hours of sleep.")
    if answers.get("CH_ALCOHOL_24H") == "Yes":
        flag("ALCOHOL", "review", 4, "Alcohol in the last 24 hours.")

    # Q5 - illness and medicines
    conditions = _list(answers.get("MH_CONDITIONS"))
    if conditions:
        flag("ILLNESS", "review", 5, "Serious or long-term illness: " + ", ".join(conditions) + ".")
    if answers.get("MH_MEDICINES") == "Yes":
        details = (answers.get("MH_MEDICINES_DETAILS") or "").strip()
        flag("MEDICINES", "review", 5, "Taking medicines now" + (f": {details}." if details else "."))

    # Q6 - tattoo, surgery, transfusion (2 years) and travel (3 years)
    events = _list(answers.get("RE_EVENTS"))
    if "Tattoo" in events:
        days = _days_since(answers.get("RE_TATTOO_DATE"), today)
        if days is None:
            flag("TATTOO_DATE_UNKNOWN", "review", 6, "Tattoo in the last 2 years; the donor doesn't remember the date.")
        elif days < TATTOO_DEFERRAL_DAYS:
            flag("TATTOO", "defer", 6, f"Tattoo {days} day(s) ago (less than 2 years).")
    if "Surgery" in events:
        flag("SURGERY", "review", 6, f"Surgery in the last 2 years (date: {answers.get('RE_SURGERY_DATE') or 'not given'}).")
    if "Blood transfusion" in events:
        flag("TRANSFUSION", "review", 6, f"Blood transfusion in the last 2 years (date: {answers.get('RE_TRANSFUSION_DATE') or 'not given'}).")
    if answers.get("TR_ABROAD") == "Yes":
        for trip in _list(answers.get("TR_TRIPS")):
            country = str(trip.get("country") or "")
            days = _days_since(trip.get("return_date"), today)
            if is_malaria_risk_country(country):
                if days is None:
                    flag("MALARIA_TRAVEL", "defer", 6, f"Travel to {country}, a malaria-risk country; return date not remembered.")
                elif days < MALARIA_TRAVEL_DAYS:
                    flag("MALARIA_TRAVEL", "defer", 6, f"Returned from {country}, a malaria-risk country, {days} day(s) ago (within 3 years).")
            elif days is None:
                flag("TRAVEL_DATE_UNKNOWN", "review", 6, f"Travel to {country}; return date not remembered.")
            elif days < OTHER_TRAVEL_DAYS:
                flag("RECENT_TRAVEL", "defer", 6, f"Returned from {country} {days} day(s) ago (within 3 months).")

    # Q7 - pregnancy (female donors) and the confidential risk-behaviour answer
    female = _list(answers.get("FD_STATUS"))
    if female:
        flag("PREGNANCY", "defer", 7, "Pregnant, breastfeeding or gave birth in the last year: " + ", ".join(female) + ".")
    if answers.get("IR_RISK") == "Yes":
        flag("RISK_BEHAVIOUR", "defer", 7, "Answered yes to the confidential risk-behaviour question.")

    severities = {f["severity"] for f in flags}
    if "defer" in severities:
        risk, recommendation = "HIGH", "Requires Doctor Review"
    elif "review" in severities:
        risk, recommendation = "MEDIUM", "Requires Doctor Review"
    else:
        risk, recommendation = "LOW", "No flags raised"
    return {"risk_level": risk, "recommendation": recommendation, "flags": flags, "age": age}
