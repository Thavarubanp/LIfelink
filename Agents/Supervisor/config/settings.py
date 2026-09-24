from pathlib import Path
from pydantic_settings import BaseSettings, SettingsConfigDict

BASE_DIR = Path(__file__).resolve().parent.parent


class Settings(BaseSettings):
    """Configuration for the LifeLink Supervisor agent (evolved from the Planning Agent, same port)."""

    HOST: str = "127.0.0.1"
    PORT: int = 8004
    VERSION: str = "2.0.0"
    LOG_LEVEL: str = "INFO"

    # Shared key: the backend calls the Supervisor with it, and the Supervisor calls the worker agents with it
    INTERNAL_SERVICE_API_KEY: str = "LifeLink-Internal-Agent-Key-2026"

    # Worker agents
    AGENT1_SCREENING_URL: str = "http://127.0.0.1:8001"   # Request Management
    AGENT2_MATCHING_URL: str = "http://127.0.0.1:8000"    # Notification
    AGENT3_INVENTORY_URL: str = "http://127.0.0.1:8003"   # Inventory Management
    HTTP_TIMEOUT_SECONDS: float = 20.0

    # Gemini (chat, intent classification, grounded answers) and embeddings (RAG)
    GOOGLE_API_KEY: str = ""
    GEMINI_API_KEY: str = ""
    MODEL_NAME: str = "gemini-2.5-flash"
    EMBEDDING_MODEL: str = "models/gemini-embedding-001"

    # Knowledge base: medical guidance and LifeLink platform guidance are kept in separate collections
    KNOWLEDGE_DIR: Path = BASE_DIR / "knowledge"
    CHROMA_DIR: Path = BASE_DIR / ".chroma"
    RETRIEVAL_TOP_K: int = 4
    # Cosine distance cut-off for vector hits. Measured with gemini-embedding-001 on this knowledge base: related
    # questions score 0.18-0.31, off-topic ones 0.46+. Re-check if the embedding model changes.
    RETRIEVAL_MAX_DISTANCE: float = 0.40

    MAX_SUPERVISOR_STEPS: int = 8

    model_config = SettingsConfigDict(env_file=".env", env_file_encoding="utf-8", extra="ignore")

    @property
    def gemini_key(self) -> str:
        return self.GOOGLE_API_KEY or self.GEMINI_API_KEY


settings = Settings()
