import logging
import uvicorn
from contextlib import asynccontextmanager
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from config.settings import settings
from api.routes import router as planning_router

logging.basicConfig(
    level=getattr(logging, settings.LOG_LEVEL.upper(), logging.INFO),
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s"
)
logger = logging.getLogger("PlanningAgentApp")


@asynccontextmanager
async def lifespan(app: FastAPI):
    logger.info("====================================================")
    logger.info("Initializing LifeLink AI Planning Agent Microservice")
    logger.info(f"Version: {settings.VERSION}")
    logger.info(f"Port: {settings.PORT}")
    logger.info(f"Downstream Agent 1 (Screening): {settings.AGENT1_SCREENING_URL}")
    logger.info(f"Downstream Agent 2 (Matching):  {settings.AGENT2_MATCHING_URL}")
    logger.info(f"Downstream Agent 3 (Inventory): {settings.AGENT3_INVENTORY_URL}")
    logger.info("====================================================")
    yield
    logger.info("Shutting down LifeLink AI Planning Agent Microservice.")


app = FastAPI(
    title="LifeLink AI Planning Agent",
    version=settings.VERSION,
    description=(
        "Central AI workflow orchestration layer integrating ASP.NET Core Backend with "
        "Agent 1 (Donor Screening), Agent 2 (Matching & Notifications), and Agent 3 (Inventory Management)."
    ),
    lifespan=lifespan
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(planning_router)


@app.get("/")
def root():
    return {
        "service": "LifeLink Planning Agent (AI Workflow Orchestrator)",
        "version": settings.VERSION,
        "docs": "/docs",
        "health": "/health",
        "planEndpoint": "/plan"
    }


if __name__ == "__main__":
    uvicorn.run("app:app", host=settings.HOST, port=settings.PORT, reload=True)
