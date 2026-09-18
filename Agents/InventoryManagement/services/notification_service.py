"""Notification Service for dispatching recommendations to backend APIs."""

import logging
from typing import List
import httpx
from config.settings import settings
from models.recommendation import Recommendation

logger = logging.getLogger("inventory_management.notification_service")


class NotificationService:
    """Service to send recommendation notifications via backend HTTP endpoints."""

    def __init__(self, backend_url: str = None, notification_endpoint: str = None, timeout: int = None):
        self.backend_url = (backend_url or settings.BACKEND_API_URL).rstrip("/")
        self.notification_endpoint = notification_endpoint or settings.NOTIFICATION_ENDPOINT
        if not self.notification_endpoint.startswith("/"):
            self.notification_endpoint = f"/{self.notification_endpoint}"
        self.timeout = timeout or settings.REQUEST_TIMEOUT

    async def send_recommendation(self, recommendation: Recommendation) -> bool:
        """Generic method to send a single recommendation payload to backend API endpoint."""
        url = f"{self.backend_url}{self.notification_endpoint}"
        payload = recommendation.to_backend_notification_payload()

        logger.info(
            f"DISPATCH_RECOMMENDATION_STARTED: Sending recommendation to facility '{recommendation.target_facility_name}' "
            f"({recommendation.recommendation_type}) via {url}"
        )

        try:
            headers = {"X-Internal-Key": getattr(settings, "INTERNAL_SERVICE_API_KEY", "LifeLink-Internal-Agent-Key-2026")}
            async with httpx.AsyncClient(timeout=self.timeout) as client:
                response = await client.post(url, json=payload, headers=headers)
                if response.status_code in (200, 201, 202):
                    logger.info(
                        f"DISPATCH_RECOMMENDATION_SUCCESS: Received status {response.status_code} "
                        f"for facility '{recommendation.target_facility_name}'"
                    )
                    return True
                else:
                    logger.warning(
                        f"DISPATCH_RECOMMENDATION_FAILED: Backend returned HTTP {response.status_code} "
                        f"for endpoint {url}: {response.text}"
                    )
                    return False
        except httpx.RequestError as e:
            logger.warning(
                f"DISPATCH_RECOMMENDATION_CONNECT_FAILED: Could not connect to backend notification API ({url}): {str(e)}. "
                "Recommendation dispatched locally in state."
            )
            return False
        except Exception as e:
            logger.error(f"DISPATCH_RECOMMENDATION_UNHANDLED_ERROR: Error sending recommendation: {str(e)}", exc_info=True)
            return False

    async def send_all_recommendations(self, recommendations: List[Recommendation]) -> bool:
        """Dispatch a list of recommendations to backend endpoints sequentially."""
        logger.info(f"DISPATCH_ALL_STARTED: Processing {len(recommendations)} recommendations...")
        if not recommendations:
            logger.info("DISPATCH_ALL_SKIPPED: No recommendations to send.")
            return True

        success_count = 0
        for rec in recommendations:
            sent = await self.send_recommendation(rec)
            if sent:
                success_count += 1

        logger.info(
            f"DISPATCH_ALL_COMPLETED: {success_count}/{len(recommendations)} recommendations "
            f"dispatched successfully to backend."
        )
        return success_count > 0 or len(recommendations) == 0
