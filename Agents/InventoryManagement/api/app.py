"""FastAPI application for the Inventory Management AI Agent."""

import asyncio
import hmac
import logging
from contextlib import asynccontextmanager
from typing import Any, Dict, List, Optional

from fastapi import Depends, FastAPI, Header, HTTPException, status
from pydantic import BaseModel

from config.settings import settings
from graph.workflow import workflow_app
from services import analysis_service

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s"
)
logger = logging.getLogger("inventory_management.api")

# Singleton guard for the optional legacy scheduler
_scheduler_task: Optional[asyncio.Task] = None
_scheduler_lock = asyncio.Lock()


async def require_internal_key(x_internal_key: str | None = Header(default=None)) -> None:
    """Only the Supervisor and the backend may call this agent."""
    expected = settings.INTERNAL_SERVICE_API_KEY
    if not expected or not x_internal_key or not hmac.compare_digest(x_internal_key, expected):
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Missing or invalid internal service key.")


async def scheduler_loop():
    """Legacy self-scheduled workflow (off by default: the backend now runs the check through the Supervisor)."""
    interval_seconds = max(60, settings.SCHEDULE_INTERVAL_MINUTES * 60)
    while True:
        try:
            await asyncio.sleep(interval_seconds)
            await workflow_app.ainvoke({"inventories": [], "shortages": [], "surpluses": [], "recommendations": [], "notifications_sent": False})
        except asyncio.CancelledError:
            break
        except Exception as e:
            logger.error(f"SCHEDULER_EXECUTION_ERROR: {str(e)}", exc_info=True)


@asynccontextmanager
async def lifespan(app: FastAPI):
    global _scheduler_task
    if settings.SCHEDULER_ENABLED:
        async with _scheduler_lock:
            if _scheduler_task is None or _scheduler_task.done():
                _scheduler_task = asyncio.create_task(scheduler_loop())
                logger.info("Legacy inventory scheduler started.")
    yield
    async with _scheduler_lock:
        if _scheduler_task and not _scheduler_task.done():
            _scheduler_task.cancel()
            try:
                await _scheduler_task
            except asyncio.CancelledError:
                pass
            _scheduler_task = None


# No CORS: browsers never call this agent directly.
app = FastAPI(
    title="LifeLink Inventory Management Agent",
    description="Detects shortages, expiring packets and emergency stock sources, and recommends transfers.",
    version="2.0.0",
    lifespan=lifespan,
)
protected = [Depends(require_internal_key)]


class AnalyzeRequest(BaseModel):
    mode: str = "monitor"  # "monitor" or "emergency"
    inventories: List[Dict[str, Any]] = []
    public_requests: List[Dict[str, Any]] = []
    emergency: Dict[str, Any] = {}
    stock_hospitals: List[Dict[str, Any]] = []


@app.get("/health")
async def health_check() -> Dict[str, str]:
    return {"status": "healthy"}


@app.post("/analyze", dependencies=protected)
async def analyze(request: AnalyzeRequest) -> Dict[str, Any]:
    """
    Supervisor entry point. Analyses the stock snapshot the backend supplied and returns recommendations only;
    it never changes inventory or sends notifications itself.
    """
    if request.mode == "emergency":
        return analysis_service.emergency(request.emergency, request.stock_hospitals)
    return analysis_service.monitor(request.inventories, request.public_requests)


@app.post("/run", dependencies=protected)
async def run_analysis() -> Dict[str, List[Any]]:
    """Legacy on-demand workflow that reads inventory from the backend itself."""
    try:
        result_state = await workflow_app.ainvoke({"inventories": [], "shortages": [], "surpluses": [], "recommendations": [], "notifications_sent": False})
        return {
            "shortages": result_state.get("shortages", []),
            "surpluses": result_state.get("surpluses", []),
            "recommendations": result_state.get("recommendations", []),
        }
    except Exception as e:
        logger.error(f"API_RUN_FAILED: {str(e)}", exc_info=True)
        raise HTTPException(status_code=500, detail="Workflow execution failed.")
