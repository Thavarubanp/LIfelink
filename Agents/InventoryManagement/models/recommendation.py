"""Recommendation models for the Inventory Management AI Agent."""

from datetime import datetime
from typing import Union, Optional
from pydantic import BaseModel, Field


class Recommendation(BaseModel):
    """Represents a generated transfer recommendation for a target facility."""

    target_facility_id: Union[int, str] = Field(..., serialization_alias="targetFacilityId")
    target_facility_name: str = Field(..., serialization_alias="targetFacilityName")
    target_facility_type: str = Field(default="Hospital", serialization_alias="targetFacilityType")
    recommendation_type: str = Field(..., serialization_alias="recommendationType")  # RECIPIENT, SOURCE, NO_SOURCE_FOUND
    related_facility_id: Optional[Union[int, str]] = Field(None, serialization_alias="relatedFacilityId")
    related_facility_name: Optional[str] = Field(None, serialization_alias="relatedFacilityName")
    related_facility_type: Optional[str] = Field(None, serialization_alias="relatedFacilityType")
    blood_group: str = Field(..., serialization_alias="bloodGroup")
    required_units: int = Field(..., serialization_alias="requiredUnits")
    available_surplus: Optional[int] = Field(None, serialization_alias="availableSurplus")
    recommended_transfer_units: Optional[int] = Field(None, serialization_alias="recommendedTransferUnits")
    title: str = Field(default="Blood Inventory Recommendation", serialization_alias="title")
    message: str = Field(..., serialization_alias="message")
    timestamp: str = Field(default_factory=lambda: datetime.utcnow().isoformat(), serialization_alias="timestamp")

    def to_backend_notification_payload(self) -> dict:
        """Converts recommendation into the backend Notification DTO format."""
        return {
            "targetFacilityId": str(self.target_facility_id),
            "hospitalId": str(self.target_facility_id),
            "recipientId": str(self.target_facility_id),
            "targetFacilityName": self.target_facility_name,
            "targetFacilityType": self.target_facility_type,
            "recommendationType": self.recommendation_type,
            "notificationType": self.recommendation_type,
            "relatedFacilityId": str(self.related_facility_id) if self.related_facility_id else None,
            "relatedFacilityName": self.related_facility_name,
            "relatedFacilityType": self.related_facility_type,
            "bloodGroup": self.blood_group,
            "requiredUnits": self.required_units,
            "availableSurplus": self.available_surplus,
            "recommendedTransferUnits": self.recommended_transfer_units,
            "title": self.title,
            "message": self.message,
            "timestamp": self.timestamp,
            "createdAt": self.timestamp,
        }
