import json

from models.donor_screening import QUESTIONS_BY_ID, SECTIONS, get_applicable_questions, render_question
from services import answer_parser, eligibility_rules
from services.report_service import deidentified_answers


def test_all_twelve_sections_are_covered():
    sections = {q.section_index for q in get_applicable_questions({"gender": "Female"}, {"DH_BEFORE": "Yes"})}
    assert sections == set(SECTIONS)


def test_female_only_and_follow_up_questions():
    male = [q.question_id for q in get_applicable_questions({"gender": "Male"}, {})]
    assert "BE_PREGNANT" not in male and "FD_MISCARRIAGE" not in male
    assert "DH_LAST_DATE" not in male and "MH_DETAILS" not in male

    female = [q.question_id for q in get_applicable_questions({"gender": "Male"}, {"P_GENDER": "Female", "DH_BEFORE": "Yes",
                                                                                   "MH_CONDITIONS": json.dumps(["Asthma"])})]
    assert "BE_PREGNANT" in female and "DH_LAST_DATE" in female and "MH_DETAILS" in female
    none_ticked = [q.question_id for q in get_applicable_questions({}, {"MH_CONDITIONS": "[]"})]
    assert "MH_DETAILS" not in none_ticked


def test_section_one_is_prefilled_for_confirmation():
    rendered = render_question(QUESTIONS_BY_ID["P_NAME"], {"fullName": "Kamal Perera"})
    assert "Kamal Perera" in rendered["text"] and "yes to confirm" in rendered["text"]
    parsed = answer_parser.parse("yes", QUESTIONS_BY_ID["P_NAME"], {"fullName": "Kamal Perera"})
    assert parsed.kind == "answer" and parsed.value == "Kamal Perera"
    corrected = answer_parser.parse("Kamal S. Perera", QUESTIONS_BY_ID["P_NAME"], {"fullName": "Kamal Perera"})
    assert corrected.value == "Kamal S. Perera"


def test_parser_understands_answers_questions_and_withdrawal():
    yes_no = QUESTIONS_BY_ID["CH_WELL"]
    assert answer_parser.parse("Yes, I feel fine", yes_no, {}).value == "Yes"
    assert answer_parser.parse("no, I haven't", QUESTIONS_BY_ID["CH_ALCOHOL_24H"], {}).value == "No"
    assert answer_parser.parse("What does this mean?", yes_no, {}).kind == "question"
    assert answer_parser.parse("I want to withdraw my donation", yes_no, {}).kind == "withdraw"
    assert answer_parser.parse("maybe", yes_no, {}).kind == "clarify"

    assert answer_parser.parse("2026-01-15", QUESTIONS_BY_ID["DH_LAST_DATE"], {}).value == "2026-01-15"
    assert answer_parser.parse("March 2025", QUESTIONS_BY_ID["DH_LAST_DATE"], {}).value == "2025-03"
    assert answer_parser.parse("twice", QUESTIONS_BY_ID["DH_COUNT"], {}).value == "2"
    assert answer_parser.parse("o positive", QUESTIONS_BY_ID["P_BLOOD_GROUP"], {}).value == "O+"

    checklist = QUESTIONS_BY_ID["CH_SYMPTOMS"]
    assert answer_parser.parse("none", checklist, {}).value == "[]"
    assert json.loads(answer_parser.parse("a cough and a cold", checklist, {}).value) == ["Cough", "Cold"]
    assert json.loads(answer_parser.parse('["Fever"]', checklist, {}).value) == ["Fever"]
    assert answer_parser.parse("same", checklist, {}, previous='["Fever"]').value == '["Fever"]'


def test_rules_flag_deferrals_and_keep_confidential_answers_out_of_messages():
    clean = eligibility_rules.evaluate({"CH_WELL": "Yes", "BE_WEIGHT_50": "Yes", "RE_EVENTS": "[]", "IR_ITEMS": "[]"})
    assert clean["risk_level"] == "LOW" and clean["recommendation"] == "Eligible"

    tattoo = eligibility_rules.evaluate({"RE_EVENTS": json.dumps(["Tattoo"])})
    assert tattoo["recommendation"] == "Temporarily Deferred" and tattoo["risk_level"] == "MEDIUM"

    recent = eligibility_rules.evaluate({"DH_LAST_DATE": "2999-01-01"})
    assert any(f["code"] == "DONATION_INTERVAL" for f in recent["flags"])

    risk = eligibility_rules.evaluate({"IR_ITEMS": json.dumps(["Injected recreational drugs"])})
    assert risk["risk_level"] == "HIGH" and risk["recommendation"] == "Requires Doctor Review"
    assert all("Injected" not in f["message"] for f in risk["flags"])


def test_llm_input_never_contains_personal_or_confidential_answers():
    answers = {"P_NAME": "Kamal Perera", "P_NIC": "199512345678", "CH_WELL": "Yes",
               "IR_ITEMS": json.dumps(["HIV/AIDS"]), "RS_SYMPTOMS": "[]"}
    text = json.dumps(deidentified_answers(answers))
    assert "Kamal" not in text and "199512345678" not in text and "HIV" not in text
    assert "feeling well" in text
