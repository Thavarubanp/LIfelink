"""Matching Service for detecting shortages, surpluses, and pairing facilities."""

import logging
from typing import List, Dict, Any
from models.inventory import InventoryRecord, ShortageRecord, SurplusRecord

logger = logging.getLogger("inventory_management.matching_service")


class MatchingService:
    """Service to evaluate inventory levels, detect shortages/surpluses, and perform matching."""

    @staticmethod
    def detect_shortages(inventories: List[InventoryRecord]) -> List[ShortageRecord]:
        """Detect facilities where CurrentUnits < MinimumThreshold."""
        logger.info(f"SHORTAGE_DETECTION_STARTED: Evaluating {len(inventories)} inventory records...")
        shortages: List[ShortageRecord] = []
        for item in inventories:
            if item.current_units < item.minimum_threshold:
                shortage_units = item.minimum_threshold - item.current_units
                record = ShortageRecord(
                    facility_id=item.facility_id,
                    facility_name=item.facility_name,
                    facility_type=item.facility_type,
                    blood_group=item.blood_group,
                    shortage_units=shortage_units,
                )
                shortages.append(record)
                logger.info(
                    f"SHORTAGE_DETECTED: {record.facility_name} ({record.facility_type}) "
                    f"needs {shortage_units} units of {record.blood_group} "
                    f"(Current: {item.current_units}, Min: {item.minimum_threshold})"
                )

        logger.info(f"SHORTAGE_DETECTION_COMPLETED: Total shortages detected = {len(shortages)}")
        return shortages

    @staticmethod
    def detect_surpluses(inventories: List[InventoryRecord]) -> List[SurplusRecord]:
        """Detect facilities where CurrentUnits > MinimumThreshold."""
        logger.info(f"SURPLUS_DETECTION_STARTED: Evaluating {len(inventories)} inventory records...")
        surpluses: List[SurplusRecord] = []
        for item in inventories:
            if item.current_units > item.minimum_threshold:
                surplus_units = item.current_units - item.minimum_threshold
                record = SurplusRecord(
                    facility_id=item.facility_id,
                    facility_name=item.facility_name,
                    facility_type=item.facility_type,
                    blood_group=item.blood_group,
                    surplus_units=surplus_units,
                )
                surpluses.append(record)
                logger.info(
                    f"SURPLUS_DETECTED: {record.facility_name} ({record.facility_type}) "
                    f"has {surplus_units} surplus units of {record.blood_group} "
                    f"(Current: {item.current_units}, Min: {item.minimum_threshold})"
                )

        logger.info(f"SURPLUS_DETECTION_COMPLETED: Total surpluses detected = {len(surpluses)}")
        return surpluses

    @staticmethod
    def match_facilities(
        shortages: List[ShortageRecord],
        surpluses: List[SurplusRecord]
    ) -> List[Dict[str, Any]]:
        """Match shortage facilities with the single surplus facility having the largest available surplus.
        
        If no surplus facility exists for a shortage, creates a match entry indicating NO_MATCH.
        """
        logger.info(
            f"MATCHING_STARTED: Processing {len(shortages)} shortages against {len(surpluses)} surpluses..."
        )
        matches: List[Dict[str, Any]] = []

        for shortage in shortages:
            # Find candidate surplus facilities with matching blood group and surplus > 0
            candidates = [
                s for s in surpluses
                if s.blood_group.upper() == shortage.blood_group.upper() and s.surplus_units > 0
            ]

            if candidates:
                # Pick the single facility with largest surplus
                best_source = max(candidates, key=lambda s: s.surplus_units)
                transfer_units = min(shortage.shortage_units, best_source.surplus_units)

                match_info = {
                    "matched": True,
                    "shortage": shortage.model_dump(by_alias=True),
                    "surplus": best_source.model_dump(by_alias=True),
                    "recommended_transfer_units": transfer_units,
                }
                matches.append(match_info)

                logger.info(
                    f"MATCH_CREATED: {shortage.facility_name} needing {shortage.shortage_units} {shortage.blood_group} "
                    f"<- Source: {best_source.facility_name} ({best_source.surplus_units} surplus available). "
                    f"Recommended Transfer = {transfer_units} units."
                )
            else:
                match_info = {
                    "matched": False,
                    "shortage": shortage.model_dump(by_alias=True),
                    "surplus": None,
                    "recommended_transfer_units": 0,
                }
                matches.append(match_info)

                logger.warning(
                    f"NO_MATCH_FOUND: No matching surplus facility found for {shortage.facility_name} "
                    f"needing {shortage.blood_group} blood."
                )

        logger.info(f"MATCHING_COMPLETED: Processed {len(matches)} match results.")
        return matches
