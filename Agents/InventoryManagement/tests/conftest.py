import os
import sys
from pathlib import Path

os.environ["INTERNAL_SERVICE_API_KEY"] = "test-key"
AGENT_DIR = Path(__file__).parent.parent.resolve()
if str(AGENT_DIR) not in sys.path:
    sys.path.insert(0, str(AGENT_DIR))
