import asyncio
import logging
from contextlib import asynccontextmanager

import uvicorn
from fastapi import FastAPI

from api.routes import router
from config.settings import settings
from knowledge.store import knowledge_store

logging.basicConfig(level=getattr(logging, settings.LOG_LEVEL.upper(), logging.INFO),
                    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s")
logger = logging.getLogger("SupervisorApp")


@asynccontextmanager
async def lifespan(app: FastAPI):
    logger.info("LifeLink Supervisor %s on %s:%s", settings.VERSION, settings.HOST, settings.PORT)
    try:
        # Embed new or changed knowledge documents (no-op without a Gemini key: keyword retrieval is used instead)
        report = await asyncio.to_thread(knowledge_store.ingest)
        logger.info("Knowledge base ready: %s", report)
    except Exception as ex:
        logger.warning("Knowledge ingestion skipped (%s); keyword retrieval will be used.", ex)
    yield


# No CORS: only the backend (server-to-server, with the internal key) calls the Supervisor.
app = FastAPI(
    title="LifeLink Supervisor Agent",
    version=settings.VERSION,
    description="Routes platform events and assistant messages to the Request Management, Notification and "
                "Inventory agents, with planning and retrieval-augmented knowledge built in.",
    lifespan=lifespan)
app.include_router(router)


@app.get("/")
def root():
    return {"service": "LifeLink Supervisor Agent", "version": settings.VERSION, "health": "/health", "plan": "/plan", "chat": "/chat"}


if __name__ == "__main__":
    uvicorn.run("app:app", host=settings.HOST, port=settings.PORT, reload=False)
