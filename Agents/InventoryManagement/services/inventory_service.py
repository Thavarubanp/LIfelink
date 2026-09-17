"""Inventory Service for fetching blood inventory levels from the backend API."""

import logging
from typing import List, Dict, Any
import httpx
from config.settings import settings
from models.inventory import InventoryRecord

logger = logging.getLogger("inventory_management.inventory_service")


class InventoryService:
    """Service to interact with the backend inventory API endpoints."""

    def __init__(self, backend_url: str = None, inventory_endpoint: str = None, timeout: int = None):
        self.backend_url = (backend_url or settings.BACKEND_API_URL).rstrip("/")
        self.inventory_endpoint = inventory_endpoint or settings.INVENTORY_ENDPOINT
        if not self.inventory_endpoint.startswith("/"):
            self.inventory_endpoint = f"/{self.inventory_endpoint}"
        self.timeout = timeout or settings.REQUEST_TIMEOUT

    async def fetch_inventory(self) -> List[InventoryRecord]:
        """Fetch all inventory records across hospitals and blood banks from backend API."""
        url = f"{self.backend_url}{self.inventory_endpoint}"
        logger.info(f"FETCH_INVENTORY_STARTED: Requesting inventory data from endpoint: {url}")

        try:
            async with httpx.AsyncClient(timeout=self.timeout) as client:
                response = await client.get(url)
                logger.info(f"FETCH_INVENTORY_RESPONSE: HTTP status {response.status_code} received from {url}")
                response.raise_for_status()
                json_body = response.json()

                # Handle ASP.NET Core ApiResponse<T> wrapper {"success": true, "data": [...]}
                if isinstance(json_body, dict):
                    data_items = json_body.get("data") or json_body.get("Data") or []
                    logger.info(f"FETCH_INVENTORY_UNWRAPPED: Extracted 'data' array from ApiResponse wrapper ({len(data_items)} items)")
                elif isinstance(json_body, list):
                    data_items = json_body
                    logger.info(f"FETCH_INVENTORY_DIRECT: Received direct JSON list ({len(data_items)} items)")
                else:
                    logger.warning(f"FETCH_INVENTORY_UNEXPECTED: Unexpected response type '{type(json_body)}' from {url}")
                    data_items = []

                records: List[InventoryRecord] = []
                for item in data_items:
                    try:
                        record = InventoryRecord.model_validate(item)
                        records.append(record)
                    except Exception as parse_err:
                        logger.warning(f"FETCH_INVENTORY_PARSE_SKIP: Failed to parse item {item}: {str(parse_err)}")

                logger.info(f"FETCH_INVENTORY_COMPLETED: Successfully parsed {len(records)} inventory records.")
                return records

        except httpx.HTTPStatusError as e:
            logger.error(f"FETCH_INVENTORY_HTTP_ERROR: Status code {e.response.status_code} for {url} - {e.response.text}")
            return []
        except httpx.RequestError as e:
            logger.warning(f"FETCH_INVENTORY_CONNECT_FAILED: Could not connect to backend API ({url}): {str(e)}")
            return []
        except Exception as e:
            logger.error(f"FETCH_INVENTORY_UNHANDLED_ERROR: Unexpected error in fetch_inventory: {str(e)}", exc_info=True)
            return []
