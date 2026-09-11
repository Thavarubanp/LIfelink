import json
import logging
import os
from typing import Dict, Any, List, Optional
from config.settings import settings
from config.prompts import MEDICAL_SCREENING_ANALYSIS_PROMPT

logger = logging.getLogger("GeminiService")

class GeminiService:
    """
    Evaluates donor answers using Google Gemini LLM with an embedded
    clinical rule-engine fallback for offline/test reliability.
    """
    def __init__(self):
        self.api_key = settings.GEMINI_API_KEY
        self.model_name = settings.MODEL_NAME

    async def analyze_screening(
        self,
        questionnaire_answers: List[Dict[str, Any]],
        donor_profile: Dict[str, Any],
        blood_request: Dict[str, Any]
    ) -> Dict[str, Any]:
        """
        Executes Gemini analysis on questionnaire answers.
        Returns dict with: risk_level, recommendation, summary, findings, flags, notes.
        """
        # Try calling Gemini LLM if API key is present
        if self.api_key:
            try:
                result = await self._call_gemini(questionnaire_answers, donor_profile, blood_request)
                if result:
                    return result
            except Exception as e:
                logger.warning(f"Gemini API invocation failed ({e}). Utilizing clinical heuristic fallback engine.")

        # Deterministic clinical heuristic rule-engine fallback
        return self._evaluate_clinical_heuristics(questionnaire_answers, donor_profile)

    async def _call_gemini(
        self,
        questionnaire_answers: List[Dict[str, Any]],
        donor_profile: Dict[str, Any],
        blood_request: Dict[str, Any]
    ) -> Optional[Dict[str, Any]]:
        # Import google.genai or fallback to REST client
        from google import genai
        client = genai.Client(api_key=self.api_key)

        prompt = MEDICAL_SCREENING_ANALYSIS_PROMPT.format(
            questionnaire_json=json.dumps(questionnaire_answers, indent=2),
            donor_dob=donor_profile.get("nic", "N/A"),
            donor_gender=donor_profile.get("gender", "Unknown"),
            blood_group=donor_profile.get("bloodGroup", "Unknown"),
            requested_blood_group=blood_request.get("bloodGroup", "Unknown"),
            units_required=blood_request.get("unitsRequired", 1)
        )

        response = client.models.generate_content(
            model=self.model_name,
            contents=prompt,
        )

        text = response.text.strip()
        # Clean markdown wrappers if present
        if text.startswith("```json"):
            text = text[7:]
        if text.startswith("```"):
            text = text[3:]
        if text.endswith("```"):
            text = text[:-3]
        text = text.strip()

        data = json.loads(text)
        # Validate schema fields
        risk_level = data.get("risk_level", "LOW").upper()
        if risk_level not in ["LOW", "MEDIUM", "HIGH"]:
            risk_level = "LOW"
            
        recommendation = data.get("recommendation", "Eligible")
        if recommendation not in ["Eligible", "Temporarily Deferred", "Requires Doctor Review"]:
            recommendation = "Requires Doctor Review"

        return {
            "risk_level": risk_level,
            "recommendation": recommendation,
            "summary": data.get("summary", "Screening completed. Review questionnaire for details."),
            "findings": data.get("findings", []),
            "flags": data.get("flags", []),
            "notes": data.get("notes", "Perform routine vital signs and physical examination.")
        }

    def _evaluate_clinical_heuristics(
        self,
        answers: List[Dict[str, Any]],
        donor_profile: Dict[str, Any]
    ) -> Dict[str, Any]:
        """
        Deterministic clinical rule-engine based on international blood banking protocols (WHO / AABB).
        Strictly produces LOW, MEDIUM, or HIGH risk with Eligible, Temporarily Deferred, or Requires Doctor Review.
        """
        flags: List[str] = []
        findings: List[str] = []
        high_risk_detected = False
        medium_risk_detected = False

        answer_map = {item.get("question_id"): (item.get("answer") or "").strip().lower() for item in answers}

        # Check Infectious Diseases (HIGH RISK)
        inf_ans = answer_map.get("INF_HISTORY", "")
        if inf_ans in ["yes", "true", "positive"]:
            high_risk_detected = True
            flags.append("Infectious disease history reported")
            findings.append("Donor reported prior positive diagnosis or contact with blood-borne infectious diseases.")

        # Check Lifestyle Risks: Injected drugs / Needle-stick / Recent tattoos (HIGH / MEDIUM)
        risk_ans = answer_map.get("RISK_LIFESTYLE", "")
        if risk_ans in ["yes", "true"]:
            high_risk_detected = True
            flags.append("Recent tattoo, piercing, or needle exposure reported")
            findings.append("Donor answered affirmatively to lifestyle or needle-stick risk exposure.")

        # Check Chronic Medical Conditions (MEDIUM / HIGH)
        chronic_ans = answer_map.get("MC_HAS_CHRONIC", "")
        if chronic_ans in ["yes", "true"]:
            medium_risk_detected = True
            flags.append("Chronic medical condition reported")
            findings.append("Donor disclosed history of chronic illness (e.g. cardiac, pulmonary, metabolic, or renal).")

        # Check Medications: Antibiotics / Blood thinners / Steroids (MEDIUM)
        med_ans = answer_map.get("MED_1", "")
        if med_ans in ["yes", "true"]:
            medium_risk_detected = True
            flags.append("Medication usage reported")
            findings.append("Donor currently takes prescription or over-the-counter medications.")

        # Check Recent Medical Procedures: Surgery, transfusion, endoscopy (MEDIUM)
        proc_ans = answer_map.get("PROC_RECENT", "")
        if proc_ans in ["yes", "true"]:
            medium_risk_detected = True
            flags.append("Recent surgery or invasive medical procedure reported")
            findings.append("Donor underwent surgical, dental, or invasive procedures in the past 12 months.")

        # Check Vaccinations (MEDIUM)
        vac_ans = answer_map.get("VAC_RECENT", "")
        if vac_ans in ["yes", "true"]:
            medium_risk_detected = True
            flags.append("Recent vaccination reported")
            findings.append("Donor received immunization within the last 4 weeks.")

        # Check Travel History (MEDIUM)
        trav_ans = answer_map.get("TRAV_RECENT", "")
        if trav_ans in ["yes", "true"]:
            medium_risk_detected = True
            flags.append("Recent travel abroad or malaria-endemic region reported")
            findings.append("Donor traveled overseas or in malaria-risk area in past 6 months.")

        # Check General Health (MEDIUM)
        if answer_map.get("GH_1", "") in ["no", "false"]:
            medium_risk_detected = True
            flags.append("Donor currently feeling unwell")
            findings.append("Donor self-reported not feeling well today.")

        if answer_map.get("GH_2", "") in ["yes", "true"] or answer_map.get("GH_3", "") in ["yes", "true"]:
            medium_risk_detected = True
            flags.append("Active or recent fever/respiratory symptoms")
            findings.append("Recent febrile or respiratory symptoms within past 4 weeks.")

        # Check Pregnancy / Breastfeeding (MEDIUM / DEFERRED)
        preg_ans = answer_map.get("PREG_STATUS", "")
        if preg_ans in ["yes", "true"]:
            medium_risk_detected = True
            flags.append("Pregnancy, recent delivery, or lactation reported")
            findings.append("Donor is currently pregnant, postpartum (<6 months), or breastfeeding.")

        # Check Donation History Complications
        if answer_map.get("DH_COMPLICATIONS", "") in ["yes", "true"]:
            flags.append("Previous donation complications reported")
            findings.append("Donor experienced previous vasovagal reaction or dizziness.")

        # Synthesize Risk Level & Recommendation
        if high_risk_detected:
            risk_level = "HIGH"
            recommendation = "Requires Doctor Review"
            summary = (
                "High clinical risk flags identified during screening. Significant exposure indicators or "
                "infectious disease history reported. In-depth medical evaluation and confirmatory testing required."
            )
            notes = "Doctor must conduct complete physical evaluation and verify donor eligibility before proceeding."
        elif medium_risk_detected:
            risk_level = "MEDIUM"
            recommendation = "Temporarily Deferred" if (vac_ans in ["yes", "true"] or proc_ans in ["yes", "true"] or preg_ans in ["yes", "true"]) else "Requires Doctor Review"
            summary = (
                "Moderate risk flags detected during questionnaire review. Donor reported recent medication, "
                "vaccination, medical procedure, or travel history requiring clinical clearance."
            )
            notes = "Verify dates of procedure/medication and confirm deferral period has concluded."
        else:
            risk_level = "LOW"
            recommendation = "Eligible"
            summary = (
                "Standard donor screening interview completed with no disqualifying medical factors or "
                "high-risk flags reported. Donor appears eligible pending routine doctor physical examination."
            )
            notes = "Check vital signs (weight >= 50kg, BP within 100-140/60-90, Hb >= 12.5 g/dL)."

        return {
            "risk_level": risk_level,
            "recommendation": recommendation,
            "summary": summary,
            "findings": findings if findings else ["No adverse health history reported."],
            "flags": flags,
            "notes": notes
        }

gemini_service = GeminiService()
