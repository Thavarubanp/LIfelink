"""FastAPI application for the Inventory Management AI Agent."""

import asyncio
import logging
from contextlib import asynccontextmanager
from typing import Dict, Any, List, Optional
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware

from config.settings import settings
from graph.workflow import workflow_app

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s"
)
logger = logging.getLogger("inventory_management.api")

# Singleton guard for the background scheduler task
_scheduler_task: Optional[asyncio.Task] = None
_scheduler_lock = asyncio.Lock()


async def scheduler_loop():
    """Background task to execute the inventory workflow periodically."""
    interval_seconds = max(60, settings.SCHEDULE_INTERVAL_MINUTES * 60)
    logger.info(
        f"SCHEDULER_STARTED: Background runner initialized. Execution interval = {settings.SCHEDULE_INTERVAL_MINUTES} minutes."
    )
    while True:
        try:
            await asyncio.sleep(interval_seconds)
            logger.info("SCHEDULER_EXECUTION_TRIGGERED: Running scheduled inventory analysis workflow...")
            initial_state = {
                "inventories": [],
                "shortages": [],
                "surpluses": [],
                "recommendations": [],
                "notifications_sent": False,
            }
            await workflow_app.ainvoke(initial_state)
            logger.info("SCHEDULER_EXECUTION_COMPLETED: Scheduled workflow finished successfully.")
        except asyncio.CancelledError:
            logger.info("SCHEDULER_CANCELLED: Scheduler task received cancellation signal. Exiting loop.")
            break
        except Exception as e:
            logger.error(f"SCHEDULER_EXECUTION_ERROR: Error during scheduled workflow: {str(e)}", exc_info=True)


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Lifecycle context manager ensuring exactly ONE background scheduler instance runs."""
    global _scheduler_task
    logger.info("LIFESPAN_STARTUP: Initializing Inventory Management AI Agent service...")

    async with _scheduler_lock:
        if _scheduler_task is None or _scheduler_task.done():
            _scheduler_task = asyncio.create_task(scheduler_loop())
            logger.info("LIFESPAN_SCHEDULER_LAUNCHED: Created background scheduler task instance.")
        else:
            logger.info("LIFESPAN_SCHEDULER_EXISTING: Background scheduler task is already running.")

    yield

    logger.info("LIFESPAN_SHUTDOWN: Stopping Inventory Management AI Agent service...")
    async with _scheduler_lock:
        if _scheduler_task and not _scheduler_task.done():
            _scheduler_task.cancel()
            try:
                await _scheduler_task
            except asyncio.CancelledError:
                pass
            _scheduler_task = None
            logger.info("LIFESPAN_SCHEDULER_STOPPED: Background scheduler task cancelled and cleaned up.")


app = FastAPI(
    title="LifeLink Inventory Management AI Agent",
    description="Student 3 AI Agent microservice for blood shortage detection, surplus matching, and recommendation generation.",
    version="1.0.0",
    lifespan=lifespan,
)

# Enable CORS for local integration & testing
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.get("/health")
async def health_check() -> Dict[str, str]:
    """Health check endpoint. Returns status 'healthy'."""
    logger.info("API_HEALTH_CHECK: Health check requested.")
    return {"status": "healthy"}


@app.post("/run")
async def run_analysis() -> Dict[str, List[Any]]:
    """Triggers the inventory analysis workflow on demand."""
    logger.info("API_RUN_REQUESTED: Manual workflow execution triggered via POST /run")
    initial_state = {
        "inventories": [],
        "shortages": [],
        "surpluses": [],
        "recommendations": [],
        "notifications_sent": False,
    }

    try:
        result_state = await workflow_app.ainvoke(initial_state)
        logger.info("API_RUN_SUCCESS: Workflow completed successfully. Returning response.")
        return {
            "shortages": result_state.get("shortages", []),
            "surpluses": result_state.get("surpluses", []),
            "recommendations": result_state.get("recommendations", []),
        }
    except Exception as e:
        logger.error(f"API_RUN_FAILED: Failed to execute workflow: {str(e)}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Workflow execution failed: {str(e)}")
