import logging
from contextlib import asynccontextmanager
from fastapi import FastAPI

from config.settings import settings
from database.db import init_db
from api.routes import router as screening_router

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(name)s: %(message)s")
logger = logging.getLogger("ScreeningAgentApp")


@asynccontextmanager
async def lifespan(app: FastAPI):
    logger.info("Initializing Request Management agent database...")
    init_db()
    logger.info("Request Management agent ready on %s:%s", settings.HOST, settings.PORT)
    yield


# No CORS: browsers never call this agent; the Supervisor relays screening turns with the internal key.
app = FastAPI(
    title="LifeLink Request Management Agent (Donor Screening)",
    version=settings.VERSION,
    description="Conducts the 12-section donor screening interview, explains questions, stores answers and submits "
                "versioned screening reports to the assigned doctor. It never approves or rejects donors.",
    lifespan=lifespan)
app.include_router(screening_router)


@app.get("/")
def root():
    return {"service": "LifeLink Request Management Agent", "health": "/api/agent/health"}
