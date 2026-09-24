import json
import logging
from typing import Any, Dict, List, Optional

from config.prompts import SCREENING_SUMMARY_PROMPT
from config.settings import settings

logger = logging.getLogger("GeminiService")


class GeminiService:
    """
    Writes the doctor-facing summary of a screening report. Gemini only ever sees de-identified answers
    (no Section 1 personal details, no confidential Section 10 answers) plus the rule flags. Without a key,
    or on any failure, a template summary built from the flags is used. The AI never approves or rejects.
    """

    def __init__(self):
        self.api_key = settings.GEMINI_API_KEY
        self.model_name = settings.MODEL_NAME

    async def summarise(self, deidentified_answers: List[Dict[str, str]], evaluation: Dict[str, Any]) -> Dict[str, str]:
        if self.api_key:
            try:
                result = await self._call_gemini(deidentified_answers, evaluation)
                if result:
                    return result
            except Exception as ex:
                logger.warning("Gemini summary failed (%s); using template summary.", type(ex).__name__)
        return self.template_summary(evaluation)

    async def _call_gemini(self, answers: List[Dict[str, str]], evaluation: Dict[str, Any]) -> Optional[Dict[str, str]]:
        from google import genai
        client = genai.Client(api_key=self.api_key)
        prompt = SCREENING_SUMMARY_PROMPT.format(
            answers_json=json.dumps(answers, indent=1),
            flags_json=json.dumps(evaluation["flags"], indent=1),
            risk_level=evaluation["risk_level"],
            recommendation=evaluation["recommendation"])
        response = await client.aio.models.generate_content(model=self.model_name, contents=prompt)
        text = (response.text or "").strip().strip("`")
        if text.lower().startswith("json"):
            text = text[4:]
        data = json.loads(text)
        summary = str(data.get("summary") or "").strip()
        if not summary:
            return None
        return {"summary": summary, "doctor_notes": str(data.get("doctor_notes") or "").strip()}

    @staticmethod
    def template_summary(evaluation: Dict[str, Any]) -> Dict[str, str]:
        flags = evaluation["flags"]
        if not flags:
            summary = "The donor reported no disqualifying answers or risk factors in the screening questionnaire."
        else:
            summary = f"The screening raised {len(flags)} point(s) for the doctor: " + " ".join(f["message"] for f in flags[:6])
        return {"summary": summary, "doctor_notes": "Confirm identity, check haemoglobin, blood pressure, pulse, temperature and weight before donation."}


gemini_service = GeminiService()
