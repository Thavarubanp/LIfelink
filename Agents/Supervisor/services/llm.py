import json
import logging
from typing import Any, Optional

from config.settings import settings

logger = logging.getLogger("Supervisor.LLM")

_chat_model = None


def message_text(message) -> str:
    """Plain text of a LangChain reply: newer Gemini models may return content as a list of blocks."""
    content = getattr(message, "content", message)
    if isinstance(content, str):
        return content
    if isinstance(content, list):
        return "".join(b if isinstance(b, str) else str(b.get("text", "")) for b in content
                       if isinstance(b, str) or (isinstance(b, dict) and b.get("type", "text") == "text"))
    return str(content or "")


def llm_available() -> bool:
    return bool(settings.gemini_key)


def _model():
    global _chat_model
    if _chat_model is None and llm_available():
        from langchain_google_genai import ChatGoogleGenerativeAI
        _chat_model = ChatGoogleGenerativeAI(model=settings.MODEL_NAME, google_api_key=settings.gemini_key, temperature=0.2)
    return _chat_model


async def complete(system: str, prompt: str) -> Optional[str]:
    """Plain-text Gemini completion; None when Gemini is not configured or fails (callers fall back to rules)."""
    model = _model()
    if model is None:
        return None
    try:
        from langchain_core.messages import HumanMessage, SystemMessage
        result = await model.ainvoke([SystemMessage(content=system), HumanMessage(content=prompt)])
        text = message_text(result).strip()
        if not text:
            logger.warning("Gemini returned an empty reply; using rule-based fallback.")
        return text or None
    except Exception as ex:
        logger.warning("Gemini call failed (%s: %s); using rule-based fallback.", type(ex).__name__, ex)
        return None


async def complete_json(system: str, prompt: str) -> Optional[Any]:
    text = await complete(system, prompt)
    if not text:
        return None
    cleaned = text.strip()
    if cleaned.startswith("```"):
        cleaned = cleaned.strip("`")
        cleaned = cleaned[4:] if cleaned.lower().startswith("json") else cleaned
    try:
        return json.loads(cleaned)
    except json.JSONDecodeError as ex:
        logger.warning("Gemini reply was not valid JSON (%s); using rule-based fallback.", ex)
        return None
