import logging
from typing import Any, Dict, Optional

import httpx

from config.settings import settings

logger = logging.getLogger("BackendClient")


class BackendUnavailableError(RuntimeError):
    """The LifeLink backend could not be reached or refused the call. Screening never continues on made-up data."""


class BackendClient:
    """
    REST client for the LifeLink ASP.NET Core backend (authenticated with the internal service key).
    There are no offline fallbacks: if the backend cannot confirm the acceptance or the donor, screening stops.
    """

    def __init__(self, base_url: Optional[str] = None):
        self.base_url = (base_url or settings.BACKEND_BASE_URL).rstrip("/")

    @property
    def headers(self) -> Dict[str, str]:
        return {"X-Internal-Key": settings.INTERNAL_SERVICE_API_KEY}

    async def _request(self, method: str, path: str, json: Optional[Dict[str, Any]] = None) -> httpx.Response:
        try:
            async with httpx.AsyncClient(timeout=10.0) as client:
                return await client.request(method, f"{self.base_url}{path}", json=json, headers=self.headers)
        except httpx.RequestError as ex:
            logger.warning("Backend unreachable for %s %s (%s)", method, path, type(ex).__name__)
            raise BackendUnavailableError("The LifeLink backend is not reachable right now.") from ex

    async def get_acceptance(self, acceptance_id: str) -> Dict[str, Any]:
        response = await self._request("GET", f"/api/Acceptances/{acceptance_id}")
        if response.status_code != 200:
            raise BackendUnavailableError(f"Acceptance {acceptance_id} could not be loaded (HTTP {response.status_code}).")
        return response.json()

    async def get_donor_profile(self, donor_user_id: str) -> Dict[str, Any]:
        response = await self._request("GET", f"/api/Auth/user/{donor_user_id}")
        if response.status_code != 200:
            raise BackendUnavailableError(f"Donor profile could not be loaded (HTTP {response.status_code}).")
        return response.json()

    async def mark_screening_started(self, acceptance_id: str) -> bool:
        response = await self._request("PUT", f"/api/Acceptances/{acceptance_id}/status?status=ScreeningPending")
        return response.status_code in (200, 204)

    async def submit_report(self, report: Dict[str, Any]) -> Dict[str, Any]:
        """Submits one immutable report version; the backend stores it and routes it to the assigned doctor."""
        response = await self._request("POST", "/api/agent/screening/report-notify", report)
        body: Dict[str, Any] = {}
        try:
            body = response.json()
        except ValueError:
            pass
        if response.status_code not in (200, 201):
            raise BackendUnavailableError(body.get("message") or f"The report was not accepted (HTTP {response.status_code}).")
        return body


backend_client = BackendClient()
