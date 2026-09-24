import os
import sys
import tempfile
from pathlib import Path

import pytest

# Isolated database and no Gemini: tests are deterministic and never touch real data
_DB_FILE = Path(tempfile.gettempdir()) / f"lifelink_screening_test_{os.getpid()}.db"
os.environ["DATABASE_URL"] = f"sqlite:///{_DB_FILE.as_posix()}"
os.environ["GEMINI_API_KEY"] = ""
os.environ["GOOGLE_API_KEY"] = ""
os.environ["INTERNAL_SERVICE_API_KEY"] = "test-key"

AGENT_DIR = Path(__file__).parent.parent.resolve()
if str(AGENT_DIR) not in sys.path:
    sys.path.insert(0, str(AGENT_DIR))

from database.db import Base, engine, init_db  # noqa: E402


@pytest.fixture(autouse=True)
def clean_database():
    Base.metadata.drop_all(bind=engine)
    init_db()
    yield
    Base.metadata.drop_all(bind=engine)
