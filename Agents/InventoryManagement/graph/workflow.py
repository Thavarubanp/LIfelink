"""LangGraph Workflow definition for the Inventory Management AI Agent."""

import logging
from typing import Dict, Any
from langgraph.graph import StateGraph, START, END

from models.state import InventoryState
from models.inventory import InventoryRecord, ShortageRecord, SurplusRecord
from models.recommendation import Recommendation
from services.inventory_service import InventoryService
from services.matching_service import MatchingService
from services.recommendation_service import RecommendationService

logger = logging.getLogger("inventory_management.workflow")


# Node 1: Fetch Inventory Data
async def fetch_inventory_node(state: InventoryState) -> Dict[str, Any]:
    """Fetch inventory records from backend API."""
    logger.info("WORKFLOW_NODE_1_START: Executing fetch_inventory_node...")
    try:
        inventory_service = InventoryService()
        records = await inventory_service.fetch_inventory()
        dict_records = [r.model_dump(by_alias=True) for r in records]
        logger.info(f"WORKFLOW_NODE_1_SUCCESS: Fetched {len(dict_records)} inventory records.")
        return {"inventories": dict_records}
    except Exception as e:
        logger.error(f"WORKFLOW_NODE_1_ERROR: Error in fetch_inventory_node: {str(e)}", exc_info=True)
        return {"inventories": []}


# Node 2: Detect Shortages
async def detect_shortages_node(state: InventoryState) -> Dict[str, Any]:
    """Detect shortages where CurrentUnits < MinimumThreshold."""
    logger.info("WORKFLOW_NODE_2_START: Executing detect_shortages_node...")
    try:
        raw_inventories = state.get("inventories", [])
        records = [InventoryRecord.model_validate(item) for item in raw_inventories]
        shortage_models = MatchingService.detect_shortages(records)
        dict_shortages = [s.model_dump(by_alias=True) for s in shortage_models]
        logger.info(f"WORKFLOW_NODE_2_SUCCESS: Detected {len(dict_shortages)} shortage records.")
        return {"shortages": dict_shortages}
    except Exception as e:
        logger.error(f"WORKFLOW_NODE_2_ERROR: Error in detect_shortages_node: {str(e)}", exc_info=True)
        return {"shortages": []}


# Node 3: Detect Surpluses
async def detect_surpluses_node(state: InventoryState) -> Dict[str, Any]:
    """Detect surplus blood where CurrentUnits > MinimumThreshold."""
    logger.info("WORKFLOW_NODE_3_START: Executing detect_surpluses_node...")
    try:
        raw_inventories = state.get("inventories", [])
        records = [InventoryRecord.model_validate(item) for item in raw_inventories]
        surplus_models = MatchingService.detect_surpluses(records)
        dict_surpluses = [s.model_dump(by_alias=True) for s in surplus_models]
        logger.info(f"WORKFLOW_NODE_3_SUCCESS: Detected {len(dict_surpluses)} surplus records.")
        return {"surpluses": dict_surpluses}
    except Exception as e:
        logger.error(f"WORKFLOW_NODE_3_ERROR: Error in detect_surpluses_node: {str(e)}", exc_info=True)
        return {"surpluses": []}


# Node 4: Match Facilities
async def match_facilities_node(state: InventoryState) -> Dict[str, Any]:
    """Match shortage facilities with the single largest surplus facility."""
    logger.info("WORKFLOW_NODE_4_START: Executing match_facilities_node...")
    try:
        raw_shortages = state.get("shortages", [])
        raw_surpluses = state.get("surpluses", [])

        shortages = [ShortageRecord.model_validate(s) for s in raw_shortages]
        surpluses = [SurplusRecord.model_validate(s) for s in raw_surpluses]

        matches = MatchingService.match_facilities(shortages, surpluses)
        logger.info(f"WORKFLOW_NODE_4_SUCCESS: Generated {len(matches)} match results.")
        return {"_matches": matches}
    except Exception as e:
        logger.error(f"WORKFLOW_NODE_4_ERROR: Error in match_facilities_node: {str(e)}", exc_info=True)
        return {"_matches": []}


# Node 5: Generate Recommendations
async def generate_recommendations_node(state: InventoryState) -> Dict[str, Any]:
    """Generate human-readable recommendation messages."""
    logger.info("WORKFLOW_NODE_5_START: Executing generate_recommendations_node...")
    try:
        matches = state.get("_matches", [])
        recommendations = RecommendationService.generate_recommendations(matches)
        dict_recommendations = [r.model_dump(by_alias=True) for r in recommendations]
        logger.info(f"WORKFLOW_NODE_5_SUCCESS: Generated {len(dict_recommendations)} recommendation messages.")
        return {"recommendations": dict_recommendations}
    except Exception as e:
        logger.error(f"WORKFLOW_NODE_5_ERROR: Error in generate_recommendations_node: {str(e)}", exc_info=True)
        return {"recommendations": []}


def build_workflow() -> Any:
    """Construct and compile the LangGraph workflow StateGraph."""
    logger.info("WORKFLOW_BUILD: Constructing LangGraph StateGraph...")
    workflow = StateGraph(InventoryState)

    # Add nodes
    workflow.add_node("fetch_inventory", fetch_inventory_node)
    workflow.add_node("detect_shortages", detect_shortages_node)
    workflow.add_node("detect_surpluses", detect_surpluses_node)
    workflow.add_node("match_facilities", match_facilities_node)
    workflow.add_node("generate_recommendations", generate_recommendations_node)

    # Add linear edges
    workflow.add_edge(START, "fetch_inventory")
    workflow.add_edge("fetch_inventory", "detect_shortages")
    workflow.add_edge("detect_shortages", "detect_surpluses")
    workflow.add_edge("detect_surpluses", "match_facilities")
    workflow.add_edge("match_facilities", "generate_recommendations")
    workflow.add_edge("generate_recommendations", END)

    compiled_graph = workflow.compile()
    logger.info("WORKFLOW_BUILD: LangGraph StateGraph compiled successfully.")
    return compiled_graph


# Export compiled graph app
workflow_app = build_workflow()
