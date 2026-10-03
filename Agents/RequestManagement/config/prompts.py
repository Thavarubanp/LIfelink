"""Prompt for the doctor-facing screening summary (de-identified input only)."""

SCREENING_SUMMARY_PROMPT = """
You are assisting a blood bank doctor in Sri Lanka by summarising a donor screening questionnaire.

Rules:
1. You must NEVER approve or reject the donor. The doctor makes 100% of the decision.
2. Use only the answers and flags below. Do not invent findings.
3. The confidential question 7 (pregnancy and risk behaviour) is represented only by its flags; do not speculate about it.
4. The rule-based risk level is {risk_level} and the recommendation is "{recommendation}". Do not change them.

De-identified answers:
{answers_json}

Rule flags:
{flags_json}

Return ONLY a JSON object:
{{
  "summary": "2-4 plain sentences for the doctor describing the relevant findings.",
  "doctor_notes": "Specific checks for the physical examination (for example haemoglobin above 12.5 g/dL, blood pressure, tattoo healing)."
}}
"""

# Reads one free-text reply into the parts of the current screening question (never used for the confidential question 7).
ANSWER_EXTRACTION_PROMPT = """
You read a blood donor's reply to one screening question and fill in the answer fields. Do not guess: leave out any
field the reply does not clearly answer. Never judge eligibility.

Question: {question}
Fields (id, label, type, allowed values):
{fields_json}

Donor's reply: "{message}"

Rules: yes_no -> "Yes" or "No"; date -> "YYYY-MM-DD" (or "Donor doesn't remember" if the donor says they do not remember
and the field allows it); number -> a number; select -> one allowed value; checklist -> a list of allowed values ([] for none);
trips -> a list of {{"country": "...", "return_date": "YYYY-MM-DD"}}.

Return ONLY a JSON object of field id -> value, for example {{"CH_WELL": "Yes"}}.
"""
