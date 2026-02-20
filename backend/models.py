from sqlalchemy import Column, Integer, String, DateTime, Text
from datetime import datetime, timezone
from database import Base

class Log(Base):
    __tablename__ = "logs"

    id = Column(Integer, primary_key=True, index=True)
    question = Column(Text, nullable=False)
    answer = Column(Text, nullable=False)
    # dodanie daty i godziny zapytania
    created_at = Column(DateTime, default=lambda: datetime.now(timezone.utc))

    ##### można tu w przyszłości dodać kolumny na meta-dane np. z którego modelu LLM pochodziła odpowiedź (jeśli to będzie ensemble) #####