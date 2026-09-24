import logging
import time
from typing import Any, Dict, Optional

import httpx

from config.settings import settings

logger = logging.getLogger("Supervisor.Clients")


class AgentClients:
    """
    HTTP clients for the three remote worker agents. Every call carries the shared internal key; failures are
    returned as {"success": False, "error": ...} so the Supervisor can degrade instead of crashing.
    """

    def __init__(self, screening_url: Optional[str] = None, notification_url: Optional[str] = None,
                 inventory_url: Optional[str] = None, timeout: Optional[float] = None):
        self.screening_url = (screening_url or settings.AGENT1_SCREENING_URL).rstrip("/")
        self.notification_url = (notification_url or settings.AGENT2_MATCHING_URL).rstrip("/")
        self.inventory_url = (inventory_url or settings.AGENT3_INVENTORY_URL).rstrip("/")
        self.timeout = timeout or settings.HTTP_TIMEOUT_SECONDS

    @property
    def _headers(self) -> Dict[str, str]:
        return {"X-Internal-Key": settings.INTERNAL_SERVICE_API_KEY}

    async def _call(self, agent: str, method: str, url: str, json: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
        try:
            async with httpx.AsyncClient(timeout=self.timeout) as client:
                response = await client.request(method, url, json=json, headers=self._headers)
                response.raise_for_status()
                return {"success": True, "agent": agent, "data": response.json(), "statusCode": response.status_code}
        except httpx.HTTPStatusError as ex:
            detail = ex.response.text[:500]
            logger.warning("%s returned HTTP %s for %s", agent, ex.response.status_code, url)
            return {"success": False, "agent": agent, "error": f"HTTP {ex.response.status_code}: {detail}", "statusCode": ex.response.status_code}
        except httpx.RequestError as ex:
            logger.warning("%s unreachable at %s (%s)", agent, url, type(ex).__name__)
            return {"success": False, "agent": agent, "error": f"{agent} is unavailable.", "statusCode": 503}
        except Exception as ex:  # defensive: a worker must never crash the Supervisor
            logger.error("Unexpected error calling %s: %s", agent, ex, exc_info=True)
            return {"success": False, "agent": agent, "error": str(ex), "statusCode": 500}

    # Request Management agent (donor screening interview)
    async def screening_start(self, acceptance_id: str) -> Dict[str, Any]:
        return await self._call("RequestManagementAgent", "POST", f"{self.screening_url}/api/agent/screening/start/{acceptance_id}", {})

    async def screening_turn(self, acceptance_id: str, message: str) -> Dict[str, Any]:
        return await self._call("RequestManagementAgent", "POST", f"{self.screening_url}/api/agent/screening/turn/{acceptance_id}", {"message": message})

    async def screening_session(self, acceptance_id: str) -> Dict[str, Any]:
        return await self._call("RequestManagementAgent", "GET", f"{self.screening_url}/api/agent/screening/session/{acceptance_id}")

    # Notification agent
    async def donor_alerts(self, payload: Dict[str, Any]) -> Dict[str, Any]:
        return await self._call("NotificationAgent", "POST", f"{self.notification_url}/process-request", payload)

    async def hospital_alerts(self, alerts: list, context: Dict[str, Any]) -> Dict[str, Any]:
        return await self._call("NotificationAgent", "POST", f"{self.notification_url}/hospital-alerts", {"alerts": alerts, "context": context})

    # Inventory agent
    async def inventory_analyze(self, payload: Dict[str, Any]) -> Dict[str, Any]:
        return await self._call("InventoryAgent", "POST", f"{self.inventory_url}/analyze", payload)

    async def check_health(self) -> Dict[str, Any]:
        targets = [
            ("RequestManagementAgent", f"{self.screening_url}/api/agent/health"),
            ("NotificationAgent", f"{self.notification_url}/health"),
            ("InventoryAgent", f"{self.inventory_url}/health"),
        ]
        results: Dict[str, Any] = {}
        async with httpx.AsyncClient(timeout=3.0) as client:
            for name, url in targets:
                started = time.perf_counter()
                try:
                    res = await client.get(url, headers=self._headers)
                    results[name] = {"url": url, "status": "REACHABLE" if res.status_code == 200 else f"HTTP_{res.status_code}",
                                     "latencyMs": round((time.perf_counter() - started) * 1000, 2), "error": None}
                except Exception as ex:
                    results[name] = {"url": url, "status": "UNREACHABLE", "latencyMs": None, "error": type(ex).__name__}
        return results


agent_clients = AgentClients()
