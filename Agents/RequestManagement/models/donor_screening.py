from typing import List, Optional, Dict, Any
from pydantic import BaseModel, Field

class QuestionDefinition(BaseModel):
    question_id: str
    section: str
    question_text: str
    question_type: str = "yes_no"  # yes_no, text, select
    options: Optional[List[str]] = None
    is_parent: bool = False
    parent_id: Optional[str] = None
    trigger_answer: Optional[str] = None  # e.g., "yes"
    female_only: bool = False

# The 12 Health Questionnaire Sections with dynamic conditional branching
QUESTION_BANK: List[QuestionDefinition] = [
    # SECTION 1: General Health
    QuestionDefinition(
        question_id="GH_1",
        section="SECTION 1: General Health",
        question_text="Are you feeling well and in good health today?",
        question_type="yes_no"
    ),
    QuestionDefinition(
        question_id="GH_2",
        section="SECTION 1: General Health",
        question_text="Have you had any fever, chills, or night sweats in the past 4 weeks?",
        question_type="yes_no"
    ),
    QuestionDefinition(
        question_id="GH_3",
        section="SECTION 1: General Health",
        question_text="Are you currently experiencing any cold, flu, persistent cough, or active sore throat?",
        question_type="yes_no"
    ),
    QuestionDefinition(
        question_id="GH_4",
        section="SECTION 1: General Health",
        question_text="Have you had significant unexplained weight loss over the past 6 months?",
        question_type="yes_no"
    ),
    QuestionDefinition(
        question_id="GH_5",
        section="SECTION 1: General Health",
        question_text="Have you been hospitalized or under intensive medical treatment within the past 12 months?",
        question_type="yes_no",
        is_parent=True
    ),
    QuestionDefinition(
        question_id="GH_5_DETAIL",
        section="SECTION 1: General Health",
        question_text="Please describe the reason and approximate date of your recent hospitalization or medical treatment:",
        question_type="text",
        parent_id="GH_5",
        trigger_answer="yes"
    ),

    # SECTION 2: Donation History
    QuestionDefinition(
        question_id="DH_1",
        section="SECTION 2: Donation History",
        question_text="Have you previously donated blood or blood products?",
        question_type="yes_no",
        is_parent=True
    ),
    QuestionDefinition(
        question_id="DH_LAST_DATE",
        section="SECTION 2: Donation History",
        question_text="When was your last blood donation date (approximate month/year)?",
        question_type="text",
        parent_id="DH_1",
        trigger_answer="yes"
    ),
    QuestionDefinition(
        question_id="DH_COMPLICATIONS",
        section="SECTION 2: Donation History",
        question_text="Did you experience any fainting, dizziness, severe hematoma, or complications during previous donations?",
        question_type="yes_no",
        parent_id="DH_1",
        trigger_answer="yes"
    ),

    # SECTION 3: Medications (Conditional Branching)
    QuestionDefinition(
        question_id="MED_1",
        section="SECTION 3: Medications",
        question_text="Are you currently taking any prescription medications or over-the-counter remedies?",
        question_type="yes_no",
        is_parent=True
    ),
    QuestionDefinition(
        question_id="MED_NAMES",
        section="SECTION 3: Medications",
        question_text="Please list your current medications and dosages:",
        question_type="text",
        parent_id="MED_1",
        trigger_answer="yes"
    ),
    QuestionDefinition(
        question_id="MED_ANTIBIOTICS",
        section="SECTION 3: Medications",
        question_text="Have you taken any antibiotics within the past 14 days?",
        question_type="yes_no",
        parent_id="MED_1",
        trigger_answer="yes"
    ),
    QuestionDefinition(
        question_id="MED_THINNERS",
        section="SECTION 3: Medications",
        question_text="Are you taking blood thinners, anticoagulants, or antiplatelet drugs (e.g. Warfarin, Aspirin, Plavix)?",
        question_type="yes_no",
        parent_id="MED_1",
        trigger_answer="yes"
    ),
    QuestionDefinition(
        question_id="MED_STEROIDS",
        section="SECTION 3: Medications",
        question_text="Have you taken oral or injectable steroids in the past 3 months?",
        question_type="yes_no",
        parent_id="MED_1",
        trigger_answer="yes"
    ),

    # SECTION 4: Medical Conditions (Conditional Branching)
    QuestionDefinition(
        question_id="MC_HAS_CHRONIC",
        section="SECTION 4: Medical Conditions",
        question_text="Have you ever been diagnosed with or treated for any major chronic medical conditions (e.g., heart disease, high blood pressure, diabetes, asthma, epilepsy, cancer, kidney/liver disease, or bleeding disorders)?",
        question_type="yes_no",
        is_parent=True
    ),
    QuestionDefinition(
        question_id="MC_DETAILS",
        section="SECTION 4: Medical Conditions",
        question_text="Which conditions have you been diagnosed with, and are they currently well-controlled under medication?",
        question_type="text",
        parent_id="MC_HAS_CHRONIC",
        trigger_answer="yes"
    ),

    # SECTION 5: Infectious Diseases (Conditional Branching)
    QuestionDefinition(
        question_id="INF_HISTORY",
        section="SECTION 5: Infectious Diseases",
        question_text="Have you ever tested positive for or had contact with Hepatitis B, Hepatitis C, HIV/AIDS, Syphilis, Tuberculosis, Malaria, Dengue, or other sexually transmitted infections?",
        question_type="yes_no",
        is_parent=True
    ),
    QuestionDefinition(
        question_id="INF_DETAILS",
        section="SECTION 5: Infectious Diseases",
        question_text="Please specify which infectious disease, the diagnosis year, and treatment outcome:",
        question_type="text",
        parent_id="INF_HISTORY",
        trigger_answer="yes"
    ),

    # SECTION 6: Medical Procedures (Conditional Branching)
    QuestionDefinition(
        question_id="PROC_RECENT",
        section="SECTION 6: Medical Procedures",
        question_text="Have you had any surgery, dental procedure, blood transfusion, organ transplant, or endoscopy in the past 12 months?",
        question_type="yes_no",
        is_parent=True
    ),
    QuestionDefinition(
        question_id="PROC_DETAILS",
        section="SECTION 6: Medical Procedures",
        question_text="Please state what procedure was performed and the approximate date/recovery status:",
        question_type="text",
        parent_id="PROC_RECENT",
        trigger_answer="yes"
    ),

    # SECTION 7: Vaccinations (Conditional Branching)
    QuestionDefinition(
        question_id="VAC_RECENT",
        section="SECTION 7: Vaccinations",
        question_text="Have you received any vaccinations within the last 4 weeks?",
        question_type="yes_no",
        is_parent=True
    ),
    QuestionDefinition(
        question_id="VAC_DETAILS",
        section="SECTION 7: Vaccinations",
        question_text="Which vaccine did you receive and on what date?",
        question_type="text",
        parent_id="VAC_RECENT",
        trigger_answer="yes"
    ),

    # SECTION 8: Travel History (Conditional Branching)
    QuestionDefinition(
        question_id="TRAV_RECENT",
        section="SECTION 8: Travel History",
        question_text="Have you traveled outside the country or visited any malaria-endemic areas in the past 6 months?",
        question_type="yes_no",
        is_parent=True
    ),
    QuestionDefinition(
        question_id="TRAV_DETAILS",
        section="SECTION 8: Travel History",
        question_text="Which countries or regions did you visit, and when did you return?",
        question_type="text",
        parent_id="TRAV_RECENT",
        trigger_answer="yes"
    ),

    # SECTION 9: Lifestyle Risks (Conditional Branching)
    QuestionDefinition(
        question_id="RISK_LIFESTYLE",
        section="SECTION 9: Lifestyle Risks",
        question_text="In the past 12 months, have you used recreational drugs, injected non-prescribed substances, received a tattoo, body piercing, acupuncture, or suffered an accidental needle-stick injury?",
        question_type="yes_no",
        is_parent=True
    ),
    QuestionDefinition(
        question_id="RISK_DETAILS",
        section="SECTION 9: Lifestyle Risks",
        question_text="Please describe the risk event and date:",
        question_type="text",
        parent_id="RISK_LIFESTYLE",
        trigger_answer="yes"
    ),

    # SECTION 10: Pregnancy (Female donors only)
    QuestionDefinition(
        question_id="PREG_STATUS",
        section="SECTION 10: Pregnancy & Women's Health",
        question_text="Are you currently pregnant, have you given birth within the past 6 months, or are you currently breastfeeding?",
        question_type="yes_no",
        female_only=True
    ),

    # SECTION 11: Allergies (Conditional Branching)
    QuestionDefinition(
        question_id="ALL_KNOWN",
        section="SECTION 11: Allergies",
        question_text="Do you have any severe allergies (e.g., to medications, latex, or antiseptic agents)?",
        question_type="yes_no",
        is_parent=True
    ),
    QuestionDefinition(
        question_id="ALL_DETAILS",
        section="SECTION 11: Allergies",
        question_text="Please list the substances or medications you are allergic to and the severity of reaction:",
        question_type="text",
        parent_id="ALL_KNOWN",
        trigger_answer="yes"
    ),

    # SECTION 12: Consent
    QuestionDefinition(
        question_id="CONS_TRUTH",
        section="SECTION 12: Consent",
        question_text="Do you confirm that all information provided in this screening is truthful and accurate to the best of your knowledge?",
        question_type="yes_no"
    ),
    QuestionDefinition(
        question_id="CONS_TEST",
        section="SECTION 12: Consent",
        question_text="Do you consent to infectious disease testing on your blood sample (including HIV, Hepatitis B/C, Syphilis, and Malaria)?",
        question_type="yes_no"
    ),
    QuestionDefinition(
        question_id="CONS_VOLUNTARY",
        section="SECTION 12: Consent",
        question_text="Do you voluntarily agree to donate blood for the designated patient/request without coercion?",
        question_type="yes_no"
    )
]

def get_applicable_questions(donor_gender: Optional[str] = None, current_answers: Optional[Dict[str, str]] = None) -> List[QuestionDefinition]:
    """
    Returns the list of active questions dynamically based on:
    1. Donor gender (skips female-only questions if donor is male).
    2. Dynamic conditional branching: skips child follow-up questions if parent answer doesn't match trigger_answer.
    """
    answers = current_answers or {}
    is_female = (donor_gender or "").strip().lower() == "female"
    
    applicable: List[QuestionDefinition] = []
    
    for q in QUESTION_BANK:
        # Check gender filter
        if q.female_only and not is_female:
            continue
            
        # Check parent trigger condition
        if q.parent_id:
            parent_answer = (answers.get(q.parent_id) or "").strip().lower()
            trigger = (q.trigger_answer or "yes").strip().lower()
            # If parent hasn't been answered yet or answer doesn't match trigger, skip
            if parent_answer != trigger:
                continue
                
        applicable.append(q)
        
    return applicable
