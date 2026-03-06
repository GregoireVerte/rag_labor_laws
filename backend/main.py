from fastapi import FastAPI, HTTPException, Depends
from fastapi.middleware.cors import CORSMiddleware
from sqlalchemy.orm import Session
from pydantic import BaseModel

from rag_engine import LaborLawRAG
from database import engine, get_db
from models import Base, Log, Session as ChatSession

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
    session_id: str = None  ## pole na ID sesji


# ENDPOINT z przywracaniem wiadomości dla danego session_id
@app.get("/history/{session_id}")
async def get_history(session_id: str, db: Session = Depends(get_db)):
    # Pobiera wszystkie logi dla tej sesji, od najstarszych
    logs = db.query(Log).filter(Log.session_id == session_id).order_by(Log.created_at.asc()).all()
    
    # Przekształca je na format, który rozumie Frontend (dymki)
    history = []
    for log in logs:
        history.append({"role": "user", "text": log.question})
        history.append({
            "role": "assistant",
            "text": log.answer,
            "sources": log.sources or []
        })
    
    return history


# ENDPOINT do pobierania wszystkich sesji
@app.get("/sessions")
async def get_all_sessions(db: Session = Depends(get_db)):
    # Pobiera wszystkie sesje posortowane od najnowszych
    return db.query(ChatSession).order_by(ChatSession.created_at.desc()).all()


# ENDPOINT do zmiany nazwy sesji
@app.patch("/sessions/{session_id}")
async def update_session_title(session_id: str, title: str, db: Session = Depends(get_db)):
    db_session = db.query(ChatSession).filter(ChatSession.id == session_id).first()
    if not db_session:
        raise HTTPException(status_code=404, detail="Sesja nie znaleziona")
    db_session.title = title
    db_session.commit()
    return db_session


# ENDPOINT Z LOGOWANIEM
@app.post("/ask")
async def ask_lawyer(query: Query, db: Session = Depends(get_db)):
    try:
        # zarządzanie sesją - sprawdzenie czy sesja już istnieje w tabeli sessions
        db_session = db.query(ChatSession).filter(ChatSession.id == query.session_id).first()

        if not db_session:
            # jeśli nie istnieje tworzy nową sesję ## domyślny tytuł to fragment pytania (pierwsze 30 znaków)
            short_title = (query.question[:30] + '...') if len(query.question) > 30 else query.question
            db_session = ChatSession(id=query.session_id, title=short_title)
            db.add(db_session)
            db.commit()

        # 1. Pobiera historię rozmowy dla danej sesji z bazy danych
        chat_history = []
        ### szuka logów z tym samym session_id posortowanych od najstarszych
        history_logs = db.query(Log).filter(
            Log.session_id == query.session_id
        ).order_by(Log.created_at.asc()).all()
        ### formatowanie logów do postaci listy krotek: [(pytanie, odpowiedź), ...]
        chat_history = [(log.question, log.answer) for log in history_logs]

        # 2. Przekazuje historię do silnika RAG ### plus uzyskuje odpowiedź od AI
        #### przekazywany jest też drugi argument: chat_history
        result = rag_engine.ask(query.question, chat_history=chat_history) ### result to słownik: {"answer": "...", "sources": [...]}
        
        # 3. Zapis nowego zapytania wraz z session_id w bazie ### utworzenie obiekt logu do zapisu w Postgres
        new_log = Log(
            session_id=query.session_id, #### zapis ID sesji
            question=query.question,
            answer=result["answer"],
            sources=result["sources"]
        )
        
        # 4. Zapis w bazie danych
        db.add(new_log)
        db.commit()
        db.refresh(new_log) ## odświeżanie by np. dostać ID z bazy
        
        # 5. Zwraca odpowiedź do frontendu
        return {
            "id": new_log.id,
            "session_id": query.session_id,
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