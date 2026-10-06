import os
import hashlib
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
    MODEL_NAME: str = os.getenv("MODEL_NAME", "gemini-3.5-flash-lite")

    BACKEND_BASE_URL: str = os.getenv("BACKEND_BASE_URL", "http://127.0.0.1:5231")
    _candidate_internal_key = os.getenv("INTERNAL_SERVICE_API_KEY", "").strip()
    INTERNAL_SERVICE_API_KEY: str = "" if hashlib.sha256(_candidate_internal_key.encode()).hexdigest() == "e5d4012d86577772245b3c13308afde74eb674e50e726303fc80931dd47b9c2d" else _candidate_internal_key

    BASE_DIR: Path = Path(__file__).resolve().parent.parent


settings = Settings()
