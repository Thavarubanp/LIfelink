import os
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Configuration settings for the LifeLink Planning Agent."""

    HOST: str = "0.0.0.0"
    PORT: int = 8004
    VERSION: str = "1.0.0"
    LOG_LEVEL: str = "INFO"

    # Downstream Agent Service Endpoints
    AGENT1_SCREENING_URL: str = "http://localhost:8001"
    AGENT2_MATCHING_URL: str = "http://localhost:8000"
    AGENT3_INVENTORY_URL: str = "http://localhost:8003"

    # HTTP Connection & Timeout Config
    HTTP_TIMEOUT_SECONDS: float = 15.0

    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore"
    )


settings = Settings()
