import os
from pathlib import Path
from dotenv import load_dotenv

load_dotenv()

class Settings:
    PROJECT_NAME: str = "LifeLink Student 1 - AI Donor Screening Agent"
    VERSION: str = "1.0.0"
    ENVIRONMENT: str = os.getenv("ENVIRONMENT", "development")
    
    HOST: str = os.getenv("HOST", "0.0.0.0")
    PORT: int = int(os.getenv("PORT", "8001"))
    
    DATABASE_URL: str = os.getenv("DATABASE_URL", "sqlite:///./screening_agent.db")
    
    GEMINI_API_KEY: str = os.getenv("GEMINI_API_KEY") or os.getenv("GOOGLE_API_KEY") or ""
    MODEL_NAME: str = os.getenv("MODEL_NAME", "gemini-2.5-flash")
    
    BACKEND_BASE_URL: str = os.getenv("BACKEND_BASE_URL", "http://localhost:5231")
    INTERNAL_SERVICE_API_KEY: str = os.getenv("INTERNAL_SERVICE_API_KEY", "LifeLink-Internal-Agent-Key-2026")
    
    BASE_DIR: Path = Path(__file__).resolve().parent.parent
    REPORTS_DIR: Path = BASE_DIR / "reports" / "generated"

settings = Settings()

# Ensure reports output directory exists
settings.REPORTS_DIR.mkdir(parents=True, exist_ok=True)
