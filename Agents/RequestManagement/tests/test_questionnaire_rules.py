import json
from datetime import date, timedelta

from models.donor_screening import DONT_REMEMBER, PARTS_BY_ID, QUESTIONS, QUESTIONS_BY_ID, missing_parts, render_question
from services import eligibility_rules, report_service
from services.answer_parser import parse_part, parse_text

TODAY = date(2026, 10, 4)
BASE = {"P_NAME": "Kamal Perera", "P_DOB": "1995-04-10", "P_GENDER": "Male", "P_WEIGHT_KG": "68", "P_CONTACT": "0771234567",
        "DH_BEFORE": "No", "CH_WELL": "Yes", "CH_INFECTION_2W": "No", "CH_MEAL_4H": "Yes", "CH_SLEEP_6H": "Yes", "CH_ALCOHOL_24H": "No",
        "MH_CONDITIONS": "[]", "MH_MEDICINES": "No", "RE_EVENTS": "[]", "TR_ABROAD": "No", "IR_RISK": "No"}


def codes(answers):
    return {f["code"]: f["severity"] for f in eligibility_rules.evaluate({**BASE, **answers}, TODAY)["flags"]}


def ago(days):
    return (TODAY - timedelta(days=days)).isoformat()


def test_exactly_seven_questions_and_no_occupation():
    assert [q.number for q in QUESTIONS] == [1, 2, 3, 4, 5, 6, 7]
    assert "P_OCCUPATION" not in PARTS_BY_ID and "P_NIC" not in PARTS_BY_ID
    assert QUESTIONS_BY_ID["Q7"].confidential


def test_pregnancy_part_only_for_female_donors_and_follow_ups_only_when_triggered():
    q7, q3, q6 = QUESTIONS_BY_ID["Q7"], QUESTIONS_BY_ID["Q3"], QUESTIONS_BY_ID["Q6"]
    assert [p.id for p in missing_parts(q7, {"gender": "Male"}, {})] == ["IR_RISK"]
    assert [p.id for p in missing_parts(q7, {"gender": "Female"}, {})] == ["FD_STATUS", "IR_RISK"]
    assert "Are you pregnant" in render_question(q7, {"gender": "Female"}, {})["text"]
    assert [p.id for p in missing_parts(q3, {}, {"DH_BEFORE": "No"})] == []
    assert [p.id for p in missing_parts(q3, {}, {"DH_BEFORE": "Yes"})] == ["DH_LAST_DATE"]
    assert [p.id for p in missing_parts(q6, {}, {"RE_EVENTS": json.dumps(["Tattoo", "Surgery"]), "TR_ABROAD": "Yes"})] == \
        ["RE_TATTOO_DATE", "RE_SURGERY_DATE", "TR_TRIPS"]


def test_no_flags_means_low_risk_and_the_ai_never_approves():
    result = eligibility_rules.evaluate(BASE, TODAY)
    assert result["flags"] == [] and result["risk_level"] == "LOW" and result["recommendation"] == "No flags raised"


def test_deferral_flags_for_the_doctor():
    assert codes({"P_DOB": "2010-01-01"})["AGE_RANGE"] == "defer"
    assert codes({"P_DOB": "1965-01-01"})["AGE_RANGE"] == "defer"                     # 61 years old in Oct 2026
    assert "AGE_RANGE" not in codes({"P_DOB": "1966-01-01", "DH_BEFORE": "Yes", "DH_LAST_DATE": ago(400)})  # 60: still eligible
    assert codes({"P_DOB": "1969-01-01", "DH_BEFORE": "No"})["FIRST_TIME_OVER_55"] == "defer"
    assert "FIRST_TIME_OVER_55" not in codes({"P_DOB": "1969-01-01", "DH_BEFORE": "Yes", "DH_LAST_DATE": ago(400)})
    assert codes({"P_WEIGHT_KG": "50"})["WEIGHT"] == "defer"
    assert "WEIGHT" not in codes({"P_WEIGHT_KG": "50.5"})
    assert codes({"DH_BEFORE": "Yes", "DH_LAST_DATE": ago(119)})["DONATION_INTERVAL"] == "defer"
    assert "DONATION_INTERVAL" not in codes({"DH_BEFORE": "Yes", "DH_LAST_DATE": ago(120)})
    assert codes({"DH_BEFORE": "Yes", "DH_LAST_DATE": DONT_REMEMBER})["LAST_DONATION_UNKNOWN"] == "info"
    assert codes({"RE_EVENTS": '["Tattoo"]', "RE_TATTOO_DATE": ago(300)})["TATTOO"] == "defer"
    assert "TATTOO" not in codes({"RE_EVENTS": '["Tattoo"]', "RE_TATTOO_DATE": ago(800)})
    assert codes({"FD_STATUS": '["Pregnant"]'})["PREGNANCY"] == "defer"
    assert codes({"IR_RISK": "Yes"})["RISK_BEHAVIOUR"] == "defer"


def test_travel_malaria_risk_within_three_years_or_any_country_within_three_months():
    trip = lambda country, days: {"TR_ABROAD": "Yes", "TR_TRIPS": json.dumps([{"country": country, "return_date": ago(days)}])}
    assert codes(trip("India", 700))["MALARIA_TRAVEL"] == "defer"
    assert "MALARIA_TRAVEL" not in codes(trip("India", 1200))
    assert codes(trip("Australia", 60))["RECENT_TRAVEL"] == "defer"
    assert codes(trip("Australia", 100)) == {}
    assert codes({"TR_ABROAD": "Yes", "TR_TRIPS": json.dumps([{"country": "Japan", "return_date": DONT_REMEMBER}])})["TRAVEL_DATE_UNKNOWN"] == "review"
    assert not eligibility_rules.is_malaria_risk_country("Sri Lanka")


def test_review_flags():
    found = codes({"CH_WELL": "No", "CH_INFECTION_2W": "Yes", "CH_MEAL_4H": "No", "CH_SLEEP_6H": "No", "CH_ALCOHOL_24H": "Yes",
                   "MH_CONDITIONS": '["Diabetes"]', "MH_MEDICINES": "Yes", "RE_EVENTS": '["Surgery", "Blood transfusion"]'})
    assert {"UNWELL_TODAY", "RECENT_INFECTION", "NO_MEAL", "LITTLE_SLEEP", "ALCOHOL", "ILLNESS", "MEDICINES", "SURGERY", "TRANSFUSION"} <= set(found)
    assert set(found.values()) == {"review"}
    result = eligibility_rules.evaluate({**BASE, "CH_WELL": "No"}, TODAY)
    assert result["risk_level"] == "MEDIUM" and result["recommendation"] == "Requires Doctor Review"


def test_parser_reads_dates_unknowns_lists_and_trips():
    assert parse_part(PARTS_BY_ID["DH_LAST_DATE"], "I can't remember")[0] == DONT_REMEMBER
    assert parse_part(PARTS_BY_ID["DH_LAST_DATE"], "2026-02-03")[0] == "2026-02-03"
    assert parse_part(PARTS_BY_ID["DH_LAST_DATE"], "2999-01-01")[1]            # future date refused
    assert parse_part(PARTS_BY_ID["P_DOB"], "I don't remember")[1]             # only some dates allow "don't remember"
    assert parse_part(PARTS_BY_ID["MH_CONDITIONS"], "none of these")[0] == "[]"
    assert json.loads(parse_part(PARTS_BY_ID["MH_CONDITIONS"], ["Diabetes", "Epilepsy"])[0]) == ["Diabetes", "Epilepsy"]
    assert parse_part(PARTS_BY_ID["P_WEIGHT_KG"], "about 72 kg")[0] == "72"
    assert parse_part(PARTS_BY_ID["P_WEIGHT_KG"], "5")[1]                      # outside 20-250
    trips = parse_part(PARTS_BY_ID["TR_TRIPS"], [{"country": "India", "return_date": "2026-05-01"}])[0]
    assert json.loads(trips) == [{"country": "India", "return_date": "2026-05-01"}]


def test_free_text_rules():
    q4 = QUESTIONS_BY_ID["Q4"]
    parsed = parse_text("yes, no, yes, yes, no", q4.parts, {})
    assert parsed.kind == "answer" and parsed.values["CH_INFECTION_2W"] == "No" and parsed.values["CH_ALCOHOL_24H"] == "No"
    assert parse_text("What is an infection?", q4.parts, {}).kind == "question"
    assert parse_text("I want to withdraw my donation", q4.parts, {}).kind == "withdraw"
    assert parse_text("feeling great", q4.parts, {}).kind == "extract"


def test_llm_never_sees_personal_details_or_question_seven():
    shared = json.dumps(report_service.deidentified_answers({**BASE, "FD_STATUS": '["Pregnant"]', "IR_RISK": "Yes"}))
    for hidden in ("Kamal", "0771234567", "1995-04-10", "risk behaviours", "Pregnant"):
        assert hidden not in shared
    assert "68 kg" in shared
