import logging
import time
from typing import Any, Dict, Optional
import httpx

from config.settings import settings

logger = logging.getLogger("PlanningAgent.Clients")


class AgentClients:
    """
    Resilient Asynchronous HTTP Client for Downstream LifeLink AI Microservices.
    Handles timeouts, retries, and network errors gracefully.
    """

    def __init__(
        self,
        agent1_url: Optional[str] = None,
        agent2_url: Optional[str] = None,
        agent3_url: Optional[str] = None,
        timeout: Optional[float] = None
    ):
        self.agent1_url = (agent1_url or settings.AGENT1_SCREENING_URL).rstrip("/")
        self.agent2_url = (agent2_url or settings.AGENT2_MATCHING_URL).rstrip("/")
        self.agent3_url = (agent3_url or settings.AGENT3_INVENTORY_URL).rstrip("/")
        self.timeout = timeout or settings.HTTP_TIMEOUT_SECONDS

    # -------------------------------------------------------------
    # AGENT 1: DONOR SCREENING AGENT
    # -------------------------------------------------------------
    async def call_screening_agent(self, acceptance_id: str, payload: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
        """
        Invokes Agent 1 to initialize a donor screening session for an acceptance.
        Endpoint: POST /api/agent/screening/start/{acceptanceId}
        """
        url = f"{self.agent1_url}/api/agent/screening/start/{acceptance_id}"
        logger.info(f"Invoking Agent 1 (Screening) at: {url}")

        try:
            async with httpx.AsyncClient(timeout=self.timeout) as client:
                response = await client.post(url, json=payload or {})
                response.raise_for_status()
                data = response.json()
                logger.info(f"Agent 1 response received for acceptance {acceptance_id}: status={data.get('status')}")
                return {
                    "success": True,
                    "agent": "ScreeningAgent",
                    "data": data,
                    "statusCode": response.status_code
                }
        except httpx.HTTPStatusError as ex:
            error_msg = f"Agent 1 returned HTTP {ex.response.status_code}: {ex.response.text}"
            logger.error(error_msg)
            return {"success": False, "agent": "ScreeningAgent", "error": error_msg, "statusCode": ex.response.status_code}
        except httpx.RequestError as ex:
            error_msg = f"Agent 1 connection failure ({type(ex).__name__}): {str(ex)}"
            logger.error(error_msg)
            return {"success": False, "agent": "ScreeningAgent", "error": error_msg, "statusCode": 503}
        except Exception as ex:
            error_msg = f"Unexpected error invoking Agent 1: {str(ex)}"
            logger.error(error_msg, exc_info=True)
            return {"success": False, "agent": "ScreeningAgent", "error": error_msg, "statusCode": 500}

    # -------------------------------------------------------------
    # AGENT 2: NOTIFICATION & MATCHING AGENT
    # -------------------------------------------------------------
    async def call_matching_agent(self, request_payload: Dict[str, Any]) -> Dict[str, Any]:
        """
        Invokes Agent 2 to match eligible donors and generate notifications.
        Endpoint: POST /process-request
        """
        url = f"{self.agent2_url}/process-request"
        logger.info(f"Invoking Agent 2 (Matching & Notifications) at: {url}")

        # Normalize payload matching Agent 2's ProcessBloodRequestInput schema
        formatted_payload = {
            "request_id": str(request_payload.get("requestId") or request_payload.get("request_id") or "REQ-UNKNOWN"),
            "blood_group": request_payload.get("bloodGroup") or request_payload.get("blood_group") or "O+",
            "units_required": int(request_payload.get("unitsRequired") or request_payload.get("units_required") or 1),
            "priority": request_payload.get("priority") or "URGENT",
            "hospital_id": str(request_payload.get("hospitalId") or request_payload.get("hospital_id") or "HOSP-DEFAULT"),
            "hospital_name": request_payload.get("hospitalName") or request_payload.get("hospital_name") or "Partner Hospital",
            "patient_reason": request_payload.get("patientReason") or request_payload.get("patient_reason") or "Transfusion Required",
            "available_donors": request_payload.get("availableDonors") or request_payload.get("available_donors") or [],
            "verified_hospital_ids": request_payload.get("verifiedHospitalIds") or request_payload.get("verified_hospital_ids") or []
        }

        try:
            async with httpx.AsyncClient(timeout=self.timeout) as client:
                response = await client.post(url, json=formatted_payload)
                response.raise_for_status()
                data = response.json()
                logger.info(f"Agent 2 response received: {len(data.get('ranked_donors', []))} donors ranked")
                return {
                    "success": True,
                    "agent": "MatchingAgent",
                    "data": data,
                    "statusCode": response.status_code
                }
        except httpx.HTTPStatusError as ex:
            error_msg = f"Agent 2 returned HTTP {ex.response.status_code}: {ex.response.text}"
            logger.error(error_msg)
            return {"success": False, "agent": "MatchingAgent", "error": error_msg, "statusCode": ex.response.status_code}
        except httpx.RequestError as ex:
            error_msg = f"Agent 2 connection failure ({type(ex).__name__}): {str(ex)}"
            logger.error(error_msg)
            return {"success": False, "agent": "MatchingAgent", "error": error_msg, "statusCode": 503}
        except Exception as ex:
            error_msg = f"Unexpected error invoking Agent 2: {str(ex)}"
            logger.error(error_msg, exc_info=True)
            return {"success": False, "agent": "MatchingAgent", "error": error_msg, "statusCode": 500}

    # -------------------------------------------------------------
    # AGENT 3: INVENTORY MANAGEMENT AGENT
    # -------------------------------------------------------------
    async def call_inventory_agent(self, payload: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
        """
        Invokes Agent 3 to detect shortages, surpluses, and inter-hospital transfer recommendations.
        Endpoint: POST /run
        """
        url = f"{self.agent3_url}/run"
        logger.info(f"Invoking Agent 3 (Inventory Management) at: {url}")

        try:
            async with httpx.AsyncClient(timeout=self.timeout) as client:
                response = await client.post(url, json=payload or {})
                response.raise_for_status()
                data = response.json()
                logger.info(
                    f"Agent 3 response received: {len(data.get('recommendations', []))} transfer recommendations"
                )
                return {
                    "success": True,
                    "agent": "InventoryAgent",
                    "data": data,
                    "statusCode": response.status_code
                }
        except httpx.HTTPStatusError as ex:
            error_msg = f"Agent 3 returned HTTP {ex.response.status_code}: {ex.response.text}"
            logger.error(error_msg)
            return {"success": False, "agent": "InventoryAgent", "error": error_msg, "statusCode": ex.response.status_code}
        except httpx.RequestError as ex:
            error_msg = f"Agent 3 connection failure ({type(ex).__name__}): {str(ex)}"
            logger.error(error_msg)
            return {"success": False, "agent": "InventoryAgent", "error": error_msg, "statusCode": 503}
        except Exception as ex:
            error_msg = f"Unexpected error invoking Agent 3: {str(ex)}"
            logger.error(error_msg, exc_info=True)
            return {"success": False, "agent": "InventoryAgent", "error": error_msg, "statusCode": 500}

    # -------------------------------------------------------------
    # HEALTH CHECKS
    # -------------------------------------------------------------
    async def check_health(self) -> Dict[str, Any]:
        """Performs health check probes against all 3 downstream AI microservices."""
        results = {}

        targets = [
            ("Agent1_Screening", f"{self.agent1_url}/api/agent/health"),
            ("Agent2_Matching", f"{self.agent2_url}/health"),
            ("Agent3_Inventory", f"{self.agent3_url}/health"),
        ]

        async with httpx.AsyncClient(timeout=3.0) as client:
            for name, health_url in targets:
                start_time = time.perf_counter()
                try:
                    res = await client.get(health_url)
                    latency = round((time.perf_counter() - start_time) * 1000, 2)
                    results[name] = {
                        "url": health_url,
                        "status": "REACHABLE" if res.status_code == 200 else f"HTTP_{res.status_code}",
                        "latencyMs": latency,
                        "error": None
                    }
                except Exception as ex:
                    results[name] = {
                        "url": health_url,
                        "status": "UNREACHABLE",
                        "latencyMs": None,
                        "error": str(ex)
                    }

        return results


agent_clients = AgentClients()
