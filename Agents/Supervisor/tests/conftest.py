import os
import sys
from pathlib import Path

import pytest

# Deterministic tests: no Gemini (rule-based routing, keyword retrieval) and a known internal key
os.environ["GOOGLE_API_KEY"] = ""
os.environ["GEMINI_API_KEY"] = ""
os.environ["INTERNAL_SERVICE_API_KEY"] = "test-key"

SUPERVISOR_DIR = Path(__file__).parent.parent.resolve()
if str(SUPERVISOR_DIR) not in sys.path:
    sys.path.insert(0, str(SUPERVISOR_DIR))

from fastapi.testclient import TestClient  # noqa: E402
from app import app  # noqa: E402

KEY = {"X-Internal-Key": "test-key"}


@pytest.fixture
def client():
    return TestClient(app)


def ok(data):
    return {"success": True, "data": data, "statusCode": 200}


def down(agent="Agent"):
    return {"success": False, "agent": agent, "error": f"{agent} is unavailable.", "statusCode": 503}
