"""Settings configuration for the Inventory Management AI Agent."""

import os
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
    INTERNAL_SERVICE_API_KEY: str = "LifeLink-Internal-Agent-Key-2026"

    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )


settings = Settings()
