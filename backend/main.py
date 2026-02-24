from fastapi import FastAPI, HTTPException, Depends
from fastapi.middleware.cors import CORSMiddleware
from sqlalchemy.orm import Session
from pydantic import BaseModel

from rag_engine import LaborLawRAG
from database import engine, get_db
from models import Base, Log

import uvicorn

# DATABASE MIGRATION
### sprawdza modele w models.py i jeśli nie ma takich tabel w bazie tworzy je
Base.metadata.create_all(bind=engine)

# Inicjalizacja
app = FastAPI(title="Labor Law RAG API with Logging")
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"], ### w produkcji podaje się tu adres danego frontendu
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)
rag_engine = LaborLawRAG()

class Query(BaseModel):
    question: str

# ENDPOINT Z LOGOWANIEM
@app.post("/ask")
async def ask_lawyer(query: Query, db: Session = Depends(get_db)):
    try:
        # 1. Uzyskuje odpowiedź od AI
        result = rag_engine.ask(query.question) ### result to słownik: {"answer": "...", "sources": [...]}
        
        # 2. Tworzy obiekt logu do zapisu w Postgres
        new_log = Log(
            question=query.question,
            answer=result["answer"]
        )
        
        # 3. Zapis w bazie danych
        db.add(new_log)
        db.commit()
        db.refresh(new_log) ## odświeżanie by np. dostać ID z bazy
        
        return {
            "id": new_log.id,
            "question": query.question,
            "answer": result["answer"],
            "sources": result["sources"], ### lista artykułów
            "timestamp": new_log.created_at
        }
        
    except Exception as e:
        db.rollback() ### w razie błędu wycofuje zmiany w bazie
        raise HTTPException(status_code=500, detail=str(e))

@app.get("/health")
async def health_check():
    return {"status": "ok", "database": "connected"}

if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=8000)