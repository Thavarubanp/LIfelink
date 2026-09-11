import logging
from contextlib import asynccontextmanager
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from config.settings import settings
from database.db import init_db
from api.routes import router as screening_router

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s"
)
logger = logging.getLogger("ScreeningAgentApp")

@asynccontextmanager
async def lifespan(app: FastAPI):
    logger.info("Initializing LifeLink Student 1 Screening Agent database...")
    init_db()
    logger.info(f"Screening Agent ready on {settings.HOST}:{settings.PORT}")
    yield
    logger.info("Shutting down LifeLink Student 1 Screening Agent.")

app = FastAPI(
    title="LifeLink Student 1 - AI Donor Screening Agent",
    version=settings.VERSION,
    description="FastAPI + LangGraph + Gemini Agent for Medical Screening, Clinical Risk Evaluation, and Doctor Reporting.",
    lifespan=lifespan
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(screening_router)

@app.get("/")
def root():
    return {
        "service": "LifeLink Student 1 AI Donor Screening Agent",
        "docs": "/docs",
        "health": "/api/agent/health"
    }
