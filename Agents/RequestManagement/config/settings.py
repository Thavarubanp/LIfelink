import os
from pathlib import Path
from dotenv import load_dotenv

load_dotenv()


class Settings:
    PROJECT_NAME: str = "LifeLink Request Management Agent (Donor Screening)"
    VERSION: str = "2.0.0"
    ENVIRONMENT: str = os.getenv("ENVIRONMENT", "development")

    # Only the Supervisor (and the backend) call this agent: listen on localhost by default
    HOST: str = os.getenv("HOST", "127.0.0.1")
    PORT: int = int(os.getenv("PORT", "8001"))

    DATABASE_URL: str = os.getenv("DATABASE_URL", "sqlite:///./screening_agent.db")

    GEMINI_API_KEY: str = os.getenv("GEMINI_API_KEY") or os.getenv("GOOGLE_API_KEY") or ""
    MODEL_NAME: str = os.getenv("MODEL_NAME", "gemini-2.5-flash")

    BACKEND_BASE_URL: str = os.getenv("BACKEND_BASE_URL", "http://127.0.0.1:5231")
    INTERNAL_SERVICE_API_KEY: str = os.getenv("INTERNAL_SERVICE_API_KEY", "LifeLink-Internal-Agent-Key-2026")

    BASE_DIR: Path = Path(__file__).resolve().parent.parent


settings = Settings()
