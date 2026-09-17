"""Recommendation Service for generating human-readable recommendation messages."""

import logging
from typing import List, Dict, Any
from config.constants import (
    RECOMMENDATION_TYPE_RECIPIENT,
    RECOMMENDATION_TYPE_SOURCE,
    RECOMMENDATION_TYPE_NO_SOURCE_FOUND,
)
from models.recommendation import Recommendation

logger = logging.getLogger("inventory_management.recommendation_service")


class RecommendationService:
    """Service to create human-readable transfer recommendations for facilities."""

    @staticmethod
    def generate_recommendations(matches: List[Dict[str, Any]]) -> List[Recommendation]:
        """Generate Recommendation Pydantic objects from matching results."""
        logger.info(f"RECOMMENDATION_GENERATION_STARTED: Processing {len(matches)} match records...")
        recommendations: List[Recommendation] = []

        for match in matches:
            shortage = match.get("shortage", {})
            surplus = match.get("surplus")
            matched = match.get("matched", False)
            transfer_units = match.get("recommended_transfer_units", 0)

            receiving_id = shortage.get("facilityId") or shortage.get("facility_id")
            receiving_name = shortage.get("facilityName") or shortage.get("facility_name")
            receiving_type = shortage.get("facilityType") or shortage.get("facility_type", "Hospital")
            blood_group = shortage.get("bloodGroup") or shortage.get("blood_group")
            shortage_units = shortage.get("shortageUnits") or shortage.get("shortage_units", 0)

            if matched and surplus:
                source_id = surplus.get("facilityId") or surplus.get("facility_id")
                source_name = surplus.get("facilityName") or surplus.get("facility_name")
                source_type = surplus.get("facilityType") or surplus.get("facility_type", "Hospital")
                surplus_units = surplus.get("surplusUnits") or surplus.get("surplus_units", 0)

                # 1. Recommendation to Shortage Facility (Recipient)
                recipient_msg = (
                    f"Your {receiving_type} inventory is below the minimum threshold for {blood_group} blood.\n\n"
                    f"Suggested source: {source_name}\n"
                    f"Available surplus: {surplus_units} units\n"
                    f"Recommended transfer: {transfer_units} units"
                )

                rec_recipient = Recommendation(
                    target_facility_id=receiving_id,
                    target_facility_name=receiving_name,
                    target_facility_type=receiving_type,
                    recommendation_type=RECOMMENDATION_TYPE_RECIPIENT,
                    related_facility_id=source_id,
                    related_facility_name=source_name,
                    related_facility_type=source_type,
                    blood_group=blood_group,
                    required_units=shortage_units,
                    available_surplus=surplus_units,
                    recommended_transfer_units=transfer_units,
                    title=f"Blood Shortage Transfer Recommendation ({blood_group})",
                    message=recipient_msg,
                )
                recommendations.append(rec_recipient)

                # 2. Recommendation to Surplus Facility (Source)
                source_msg = (
                    f"{receiving_name} requires {blood_group} blood.\n"
                    f"Your facility currently has surplus stock.\n"
                    f"Suggested transfer: {transfer_units} units."
                )

                rec_source = Recommendation(
                    target_facility_id=source_id,
                    target_facility_name=source_name,
                    target_facility_type=source_type,
                    recommendation_type=RECOMMENDATION_TYPE_SOURCE,
                    related_facility_id=receiving_id,
                    related_facility_name=receiving_name,
                    related_facility_type=receiving_type,
                    blood_group=blood_group,
                    required_units=shortage_units,
                    available_surplus=surplus_units,
                    recommended_transfer_units=transfer_units,
                    title=f"Surplus Blood Transfer Request ({blood_group})",
                    message=source_msg,
                )
                recommendations.append(rec_source)

                logger.info(
                    f"RECOMMENDATION_PAIR_GENERATED: Created recipient & source recommendations "
                    f"for {receiving_name} <-> {source_name} ({transfer_units} units {blood_group})"
                )

            else:
                # Fallback: No matching surplus facility found
                no_source_msg = (
                    f"Your {receiving_type} inventory is below the minimum threshold for {blood_group} blood.\n"
                    f"No suitable source facility with surplus stock was found at this time."
                )

                rec_no_source = Recommendation(
                    target_facility_id=receiving_id,
                    target_facility_name=receiving_name,
                    target_facility_type=receiving_type,
                    recommendation_type=RECOMMENDATION_TYPE_NO_SOURCE_FOUND,
                    related_facility_id=None,
                    related_facility_name=None,
                    related_facility_type=None,
                    blood_group=blood_group,
                    required_units=shortage_units,
                    available_surplus=0,
                    recommended_transfer_units=0,
                    title=f"Blood Shortage Notice - No Source Available ({blood_group})",
                    message=no_source_msg,
                )
                recommendations.append(rec_no_source)

                logger.info(
                    f"RECOMMENDATION_FALLBACK_GENERATED: Created NO_SOURCE_FOUND recommendation "
                    f"for {receiving_name} ({blood_group})"
                )

        logger.info(f"RECOMMENDATION_GENERATION_COMPLETED: Generated {len(recommendations)} recommendation records.")
        return recommendations
