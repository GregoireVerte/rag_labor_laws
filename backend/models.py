from sqlalchemy import Column, Integer, String, DateTime, Text, JSON, ForeignKey
from sqlalchemy.orm import relationship
from datetime import datetime, timezone
from database import Base

class Session(Base):
    __tablename__ = "sessions"

    id = Column(String, primary_key=True, index=True)
    title = Column(String, nullable=False)
    created_at = Column(DateTime, default=lambda: datetime.now(timezone.utc))

    # Relacja: jedna sesja ma wiele logów
    logs = relationship("Log", back_populates="session", cascade="all, delete-orphan")

class Log(Base):
    __tablename__ = "logs"

    id = Column(Integer, primary_key=True, index=True)
    session_id = Column(String, ForeignKey("sessions.id"), index=True)
    question = Column(Text, nullable=False)
    answer = Column(Text, nullable=False)
    # dodanie daty i godziny zapytania
    created_at = Column(DateTime, default=lambda: datetime.now(timezone.utc))
    sources = Column(JSON, nullable=True)

    # Relacja zwrotna
    session = relationship("Session", back_populates="logs")

    ##### można tu w przyszłości dodać kolumny na meta-dane np. z którego modelu LLM pochodziła odpowiedź (jeśli to będzie ensemble) #####