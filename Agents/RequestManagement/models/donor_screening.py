"""
LifeLink donor screening questionnaire (12 sections).

Question types:
  confirm   - Section 1 details pre-filled from the donor's profile; "yes" keeps the value, anything else replaces it
  yes_no    - yes / no
  text      - free text
  date      - a date (YYYY-MM-DD or YYYY-MM)
  number    - a whole number
  select    - one of `options`
  checklist - any of `options` (or "None"); a follow-up question asks for details of the ticked items

Section 10 is confidential: its answers are parsed by fixed rules only and never sent to an LLM.
Pregnancy-related questions are asked once (Section 4) and only to female donors; the report repeats them in Section 12.
"""
from typing import Dict, List, Optional
from pydantic import BaseModel

YES_NO = ["Yes", "No"]


class QuestionDefinition(BaseModel):
    question_id: str
    section: str
    section_index: int
    question_text: str
    question_type: str = "yes_no"
    options: Optional[List[str]] = None
    help: Optional[str] = None
    parent_id: Optional[str] = None
    trigger_answer: Optional[str] = None  # "yes", or "any" for checklist parents with at least one item ticked
    female_only: bool = False
    prefill_key: Optional[str] = None
    confidential: bool = False


SECTIONS: Dict[int, str] = {
    1: "Personal Information",
    2: "Previous Donation History",
    3: "Current Health Status",
    4: "Basic Eligibility",
    5: "Medical History",
    6: "Recent Medical Events (past 12 months)",
    7: "Recent Diseases (past 12 months)",
    8: "Dental & Medication History",
    9: "Travel History",
    10: "Infectious Disease Risk Assessment (confidential)",
    11: "Recent Symptoms (past 6 months)",
    12: "Female Donors",
}

MEDICAL_CONDITIONS = [
    "Heart disease", "Heart surgery", "Stroke", "High blood pressure", "Low blood pressure", "Asthma",
    "Chronic lung disease", "Tuberculosis", "Diabetes", "Thyroid disease", "Kidney disease", "Liver disease",
    "Cancer", "Epilepsy", "Seizures", "Mental illness", "Blood disorders", "Bleeding disorders",
    "G6PD deficiency", "Polycythemia", "Leprosy", "Syphilis", "Gonorrhea", "Severe allergies",
]
RECENT_EVENTS = [
    "Blood transfusion", "Surgery", "Hospitalization", "Serious accident", "Vaccination", "Rabies treatment",
    "Acupuncture", "Tattoo", "Piercing", "Imprisonment",
]
RECENT_DISEASES = [
    "Jaundice", "Hepatitis B", "Hepatitis C", "Typhoid", "Tuberculosis", "Malaria", "Dengue", "Chickenpox",
    "Measles", "COVID-19", "Other infectious disease",
]
DENTAL_MEDICATION = [
    "Dental procedure", "Antibiotics", "Prescription medication", "Blood thinners", "Steroids", "Medication for an infection",
]
INFECTION_RISKS = [
    "HIV/AIDS", "Hepatitis B", "Hepatitis C", "Syphilis", "Concern about possible HIV exposure", "Injected recreational drugs",
    "Shared needles", "High-risk sexual behaviour", "Diagnosed with a sexually transmitted infection",
    "Partner with a sexually transmitted infection or concern",
]
RECENT_SYMPTOMS = ["Persistent fever", "Night sweats", "Unexplained weight loss", "Diarrhea", "Swollen lymph nodes", "Unusual fatigue"]
CURRENT_SYMPTOMS = ["Fever", "Cough", "Cold", "Sore throat", "Flu symptoms"]
COMPLICATIONS = ["Fainting", "Dizziness", "Excessive bleeding", "Allergic reaction", "Other"]


def _q(qid: str, section: int, text: str, qtype: str = "yes_no", **kwargs) -> QuestionDefinition:
    options = kwargs.pop("options", YES_NO if qtype == "yes_no" else None)
    return QuestionDefinition(question_id=qid, section=SECTIONS[section], section_index=section, question_text=text,
                              question_type=qtype, options=options, **kwargs)


QUESTION_BANK: List[QuestionDefinition] = [
    # Section 1 - Personal Information (pre-filled from the profile where LifeLink has it)
    _q("P_NAME", 1, "Is your full name {value}?", "confirm", prefill_key="fullName"),
    _q("P_NIC", 1, "What is your NIC or passport number?", "text", help="Your National Identity Card (or passport) number, used by the blood bank to identify you."),
    _q("P_DOB", 1, "What is your date of birth?", "date", prefill_key="dateOfBirth", help="Use the format YYYY-MM-DD. Your age is worked out from it."),
    _q("P_GENDER", 1, "What is your gender?", "select", options=["Male", "Female", "Other"], prefill_key="gender",
       help="Some questions (such as pregnancy) are only asked to female donors."),
    _q("P_ADDRESS", 1, "Is your address {value}?", "confirm", prefill_key="address"),
    _q("P_MOBILE", 1, "Is your mobile number {value}?", "confirm", prefill_key="phoneNumber"),
    _q("P_EMAIL", 1, "Is your email address {value}?", "confirm", prefill_key="email"),
    _q("P_BLOOD_GROUP", 1, "What is your blood group, if you know it?", "select",
       options=["A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-", "Don't know"], prefill_key="bloodGroup",
       help="If you are not sure, choose \"Don't know\"; the blood bank tests it when you donate."),
    _q("P_OCCUPATION", 1, "What is your occupation?", "text",
       help="Some jobs (for example pilots, drivers of public transport or people working at heights) need a rest period after donating."),
    _q("P_EMERGENCY_NAME", 1, "Who should we contact in an emergency? (name)", "text"),
    _q("P_EMERGENCY_PHONE", 1, "What is your emergency contact's phone number?", "text"),

    # Section 2 - Previous Donation History
    _q("DH_BEFORE", 2, "Have you donated blood before?"),
    _q("DH_LAST_DATE", 2, "When was your last donation? (YYYY-MM-DD, or YYYY-MM if you only know the month)", "date", parent_id="DH_BEFORE", trigger_answer="yes"),
    _q("DH_COUNT", 2, "About how many times have you donated blood?", "number", parent_id="DH_BEFORE", trigger_answer="yes"),
    _q("DH_COMPLICATIONS", 2, "Did you have any of these problems after a donation? Choose any that apply, or None.", "checklist",
       options=COMPLICATIONS, parent_id="DH_BEFORE", trigger_answer="yes",
       help="Problems during or after a previous donation help the staff take extra care this time."),
    _q("DH_COMPLICATIONS_DETAILS", 2, "Please describe the problem you had after donating.", "text", parent_id="DH_COMPLICATIONS", trigger_answer="any"),
    _q("DH_ADVISED_NOT", 2, "Has a doctor or blood bank ever advised you not to donate blood?"),
    _q("DH_120_DAYS", 2, "Have at least 120 days (about 4 months) passed since your last donation?", parent_id="DH_BEFORE", trigger_answer="yes",
       help="Whole blood donors must wait at least 120 days so the body can rebuild its red cells and iron."),

    # Section 3 - Current Health Status
    _q("CH_WELL", 3, "Are you feeling well today?"),
    _q("CH_ATE_4H", 3, "Have you eaten a meal in the last 4 hours?", help="Donating on an empty stomach makes fainting more likely."),
    _q("CH_SLEPT_6H", 3, "Did you sleep at least 6 hours last night?"),
    _q("CH_SYMPTOMS", 3, "Do you have any of these right now? Choose any that apply, or None.", "checklist", options=CURRENT_SYMPTOMS),
    _q("CH_TREATMENT", 3, "Are you currently under medical treatment?"),
    _q("CH_TREATMENT_DETAILS", 3, "What treatment are you receiving, and for what?", "text", parent_id="CH_TREATMENT", trigger_answer="yes"),
    _q("CH_ALCOHOL_24H", 3, "Have you had alcohol in the last 24 hours?"),

    # Section 4 - Basic Eligibility
    _q("BE_AGE_18_60", 4, "Are you between 18 and 60 years old?"),
    _q("BE_WEIGHT_50", 4, "Do you weigh more than 50 kg?"),
    _q("BE_PREGNANT", 4, "Are you pregnant?", female_only=True),
    _q("BE_BREASTFEEDING", 4, "Are you breastfeeding?", female_only=True),
    _q("BE_ABORTION_6M", 4, "Have you had an abortion in the last 6 months?", female_only=True),

    # Section 5 - Medical History
    _q("MH_CONDITIONS", 5, "Have you ever had any of these conditions? Choose any that apply, or None.", "checklist", options=MEDICAL_CONDITIONS,
       help="These conditions can affect your safety when donating or the safety of the patient. Ask about any term you don't know."),
    _q("MH_DETAILS", 5, "Please give details for each condition you selected (when it was diagnosed and any treatment).", "text",
       parent_id="MH_CONDITIONS", trigger_answer="any"),

    # Section 6 - Recent Medical Events (past 12 months)
    _q("RE_EVENTS", 6, "In the past 12 months, have you had any of these? Choose any that apply, or None.", "checklist", options=RECENT_EVENTS),
    _q("RE_DETAILS", 6, "Please give details and approximate dates for the items you selected.", "text", parent_id="RE_EVENTS", trigger_answer="any"),

    # Section 7 - Recent Diseases (past 12 months)
    _q("RD_DISEASES", 7, "In the past 12 months, have you had any of these illnesses? Choose any that apply, or None.", "checklist", options=RECENT_DISEASES),
    _q("RD_DETAILS", 7, "Please give details and approximate dates for the illnesses you selected.", "text", parent_id="RD_DISEASES", trigger_answer="any"),
    _q("RD_HEPATITIS_CONTACT", 7, "Have you been in close contact with someone who had hepatitis or jaundice in the past 12 months?"),
    _q("RD_ANTIMALARIAL_3Y", 7, "Have you taken anti-malarial medication in the last 3 years?"),

    # Section 8 - Dental & Medication History
    _q("DM_ITEMS", 8, "Recently, have you had or taken any of these? Choose any that apply, or None.", "checklist", options=DENTAL_MEDICATION,
       help="Dental treatment and some medicines (for example antibiotics or blood thinners) can mean waiting before donating."),
    _q("DM_MEDICATIONS", 8, "Please list any medicines you currently take (or type None).", "text"),

    # Section 9 - Travel History
    _q("TR_ABROAD", 9, "Have you travelled outside Sri Lanka in the past 3 years?"),
    _q("TR_MALARIA", 9, "Did you travel to a country where malaria is common?", parent_id="TR_ABROAD", trigger_answer="yes"),
    _q("TR_COUNTRIES", 9, "Which countries did you visit?", "text", parent_id="TR_ABROAD", trigger_answer="yes"),
    _q("TR_DATES", 9, "When did you travel? (approximate dates)", "text", parent_id="TR_ABROAD", trigger_answer="yes"),

    # Section 10 - Infectious Disease Risk Assessment (confidential)
    _q("IR_ITEMS", 10, "This section is confidential and seen only by the doctor. Does any of the following apply to you? Choose any that apply, or None.",
       "checklist", options=INFECTION_RISKS, confidential=True,
       help="These questions are asked of every donor because some infections cannot be detected straight after exposure. Your answer is private and does not judge you."),

    # Section 11 - Recent Symptoms (past 6 months)
    _q("RS_SYMPTOMS", 11, "In the past 6 months, have you had any of these? Choose any that apply, or None.", "checklist", options=RECENT_SYMPTOMS),

    # Section 12 - Female Donors (pregnancy, breastfeeding and abortion were asked in Section 4)
    _q("FD_DELIVERED_1Y", 12, "Have you given birth in the past 12 months?", female_only=True),
    _q("FD_MISCARRIAGE", 12, "Have you had a miscarriage in the past 6 months?", female_only=True),

    # Consent
    _q("CONSENT", 12, "Do you confirm your answers are true and agree to share this report with the reviewing doctor?"),
]

QUESTIONS_BY_ID = {q.question_id: q for q in QUESTION_BANK}


def _is_female(profile: Dict, answers: Dict[str, str]) -> bool:
    gender = answers.get("P_GENDER") or profile.get("gender") or ""
    return gender.strip().lower() == "female"


def get_applicable_questions(profile: Optional[Dict] = None, answers: Optional[Dict[str, str]] = None) -> List[QuestionDefinition]:
    """Questions that apply to this donor right now (gender and follow-up rules applied to the answers so far)."""
    profile = profile or {}
    answers = answers or {}
    female = _is_female(profile, answers)
    applicable: List[QuestionDefinition] = []
    for q in QUESTION_BANK:
        if q.female_only and not female:
            continue
        if q.parent_id:
            parent_answer = (answers.get(q.parent_id) or "").strip().lower()
            if q.trigger_answer == "any":
                if parent_answer in ("", "none", "[]"):
                    continue
            elif parent_answer != (q.trigger_answer or "yes"):
                continue
        applicable.append(q)
    return applicable


def render_question(q: QuestionDefinition, profile: Dict, previous: Optional[str] = None) -> Dict:
    """The question as shown to the donor, with pre-filled values and the previous answer when updating."""
    text = q.question_text
    prefill = profile.get(q.prefill_key) if q.prefill_key else None
    if q.question_type == "confirm":
        if prefill:
            text = text.replace("{value}", str(prefill)) + " Reply yes to confirm, or type the correct value."
        else:
            text = text.replace("Is your", "What is your").replace(" {value}", "").rstrip("?") + "?"
    elif prefill and q.question_type in ("date", "select"):
        text += f" (Your profile says: {prefill}. Reply yes to keep it.)"
    if previous:
        text += f" (Your previous answer: {previous}. Reply \"same\" to keep it.)"
    return {
        "question_id": q.question_id,
        "section": q.section,
        "section_index": q.section_index,
        "text": text,
        "type": q.question_type,
        "options": q.options,
        "confidential": q.confidential,
        "help": q.help,
    }
