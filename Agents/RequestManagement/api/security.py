import hmac
from fastapi import Header, HTTPException, status

from config.settings import settings


async def require_internal_key(x_internal_key: str | None = Header(default=None)) -> None:
    """Screening data is health data: only the Supervisor and the backend (with the internal key) may call this agent."""
    expected = settings.INTERNAL_SERVICE_API_KEY
    if not expected or not x_internal_key or not hmac.compare_digest(x_internal_key, expected):
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Missing or invalid internal service key.")
