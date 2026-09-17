"""Inventory models for the Inventory Management AI Agent."""

from typing import Union, Optional
from pydantic import BaseModel, Field, AliasChoices


class InventoryRecord(BaseModel):
    """Represents an inventory record for a Hospital or Blood Bank facility.
    
    Supports ASP.NET Core backend DTO structure (UnitsAvailable, MinimumThreshold, HospitalId, HospitalName)
    as well as generic facility properties.
    """

    facility_id: Union[int, str] = Field(
        ...,
        validation_alias=AliasChoices(
            "facility_id", "facilityId", "FacilityId",
            "hospital_id", "hospitalId", "HospitalId",
            "inventory_id", "inventoryId", "InventoryId"
        ),
        serialization_alias="facilityId"
    )
    facility_name: str = Field(
        default="Unknown Facility",
        validation_alias=AliasChoices(
            "facility_name", "facilityName", "FacilityName",
            "hospital_name", "hospitalName", "HospitalName"
        ),
        serialization_alias="facilityName"
    )
    facility_type: str = Field(
        default="Hospital",
        validation_alias=AliasChoices(
            "facility_type", "facilityType", "FacilityType"
        ),
        serialization_alias="facilityType"
    )
    blood_group: str = Field(
        ...,
        validation_alias=AliasChoices(
            "blood_group", "bloodGroup", "BloodGroup"
        ),
        serialization_alias="bloodGroup"
    )
    current_units: int = Field(
        ...,
        validation_alias=AliasChoices(
            "current_units", "currentUnits", "CurrentUnits",
            "units_available", "unitsAvailable", "UnitsAvailable",
            "quantity", "Quantity"
        ),
        serialization_alias="currentUnits"
    )
    minimum_threshold: int = Field(
        ...,
        validation_alias=AliasChoices(
            "minimum_threshold", "minimumThreshold", "MinimumThreshold",
            "threshold", "Threshold"
        ),
        serialization_alias="minimumThreshold"
    )

    # Backward compatibility properties
    @property
    def hospital_id(self) -> Union[int, str]:
        return self.facility_id

    @property
    def hospital_name(self) -> str:
        return self.facility_name


class ShortageRecord(BaseModel):
    """Represents a detected blood shortage at a facility."""

    facility_id: Union[int, str] = Field(..., serialization_alias="facilityId")
    facility_name: str = Field(..., serialization_alias="facilityName")
    facility_type: str = Field(default="Hospital", serialization_alias="facilityType")
    blood_group: str = Field(..., serialization_alias="bloodGroup")
    shortage_units: int = Field(..., serialization_alias="shortageUnits")

    @property
    def hospital_id(self) -> Union[int, str]:
        return self.facility_id

    @property
    def hospital_name(self) -> str:
        return self.facility_name


class SurplusRecord(BaseModel):
    """Represents detected surplus blood units at a facility."""

    facility_id: Union[int, str] = Field(..., serialization_alias="facilityId")
    facility_name: str = Field(..., serialization_alias="facilityName")
    facility_type: str = Field(default="Hospital", serialization_alias="facilityType")
    blood_group: str = Field(..., serialization_alias="bloodGroup")
    surplus_units: int = Field(..., serialization_alias="surplusUnits")

    @property
    def hospital_id(self) -> Union[int, str]:
        return self.facility_id

    @property
    def hospital_name(self) -> str:
        return self.facility_name
