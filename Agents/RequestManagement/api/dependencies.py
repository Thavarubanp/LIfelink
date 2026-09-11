from fastapi import Depends
from sqlalchemy.orm import Session
from database.db import get_db

def get_database_session(db: Session = Depends(get_db)) -> Session:
    return db
