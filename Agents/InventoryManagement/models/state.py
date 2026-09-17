"""LangGraph State definition for the Inventory Management AI Agent."""

from typing import List, Dict, Any, TypedDict


class InventoryState(TypedDict):
    """LangGraph State dictionary structure."""

    inventories: List[Dict[str, Any]]
    shortages: List[Dict[str, Any]]
    surpluses: List[Dict[str, Any]]
    recommendations: List[Dict[str, Any]]
    notifications_sent: bool
