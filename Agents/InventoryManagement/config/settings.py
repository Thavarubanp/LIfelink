"""Settings configuration for the Inventory Management AI Agent."""

import os
import hashlib
from pydantic import field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict
from config.constants import (
    DEFAULT_BACKEND_API_URL,
    DEFAULT_INVENTORY_ENDPOINT,
    DEFAULT_NOTIFICATION_ENDPOINT,
    DEFAULT_REQUEST_TIMEOUT,
    DEFAULT_SCHEDULE_INTERVAL_MINUTES,
)


class Settings(BaseSettings):
    """Application settings loaded from environment variables or .env file."""

    BACKEND_API_URL: str = DEFAULT_BACKEND_API_URL
    INVENTORY_ENDPOINT: str = DEFAULT_INVENTORY_ENDPOINT
    NOTIFICATION_ENDPOINT: str = DEFAULT_NOTIFICATION_ENDPOINT
    REQUEST_TIMEOUT: int = DEFAULT_REQUEST_TIMEOUT
    SCHEDULE_INTERVAL_MINUTES: int = DEFAULT_SCHEDULE_INTERVAL_MINUTES
    INTERNAL_SERVICE_API_KEY: str = ""
    # The backend runs inventory checks through the Supervisor; the agent's own scheduler is opt-in
    SCHEDULER_ENABLED: bool = False

    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )

    @field_validator("INTERNAL_SERVICE_API_KEY")
    @classmethod
    def reject_retired_internal_key(cls, value: str) -> str:
        value = value.strip()
        return "" if hashlib.sha256(value.encode()).hexdigest() == "e5d4012d86577772245b3c13308afde74eb674e50e726303fc80931dd47b9c2d" else value


settings = Settings()
