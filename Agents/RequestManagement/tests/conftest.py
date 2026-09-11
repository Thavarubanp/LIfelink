import pytest
from database.db import Base, engine, init_db

@pytest.fixture(autouse=True)
def clean_database():
    """Ensure a clean database state before each test run."""
    Base.metadata.drop_all(bind=engine)
    init_db()
    yield
    Base.metadata.drop_all(bind=engine)
