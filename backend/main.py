from fastapi import FastAPI, HTTPException, Depends, Request, Response
from fastapi.middleware.cors import CORSMiddleware
from sqlalchemy.orm import Session
from pydantic import BaseModel
import os
import requests

### plus importy skryptów do aktualizacji bazy wiedzy
from labor_code_ingestion_pipeline import get_latest_labor_code_automated, download_specific_unified_text, save_metadata
from ingest_to_cloud import run_ingestion

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


# ENDPOINT do usuwania sesji
@app.delete("/sessions/{session_id}")
async def delete_session(session_id: str, db: Session = Depends(get_db)):
    db_session = db.query(ChatSession).filter(ChatSession.id == session_id).first()
    if not db_session:
        raise HTTPException(status_code=404, detail="Sesja nie znaleziona")
    
    db.delete(db_session)
    db.commit()
    return {"message": "Sesja i powiązane logi zostały usunięte"}


# ENDPOINT DO AKTUALIZACJI BAZY WIEDZY ### ten endpoint będzie wywoływany przez n8n aby sprawdzić i pobrać nowe prawo
@app.post("/api/v1/admin/update-knowledge")
async def update_legal_knowledge(request: Request):
    # 1. Proste zabezpieczenie (API Key ze zmiennych środowiskowych)
    ## w n8n trzeba dodać nagłówek X-Admin-Key
    admin_key = request.headers.get("X-Admin-Key")
    ### sprawdza klucz z .env lub używa placeholder'a do testów
    if admin_key != os.environ.get("ADMIN_API_KEY", "super-tajne-haslo-testowe"):
        raise HTTPException(status_code=403, detail="Brak uprawnień administratora")

    try:
        print("--- ROZPOCZĘTO SPRAWDZANIE AKTUALIZACJI PRAWA ---")
        
        # 2. Uruchomienie Pipeline: Sprawdzenie czy jest nowa wersja
        url, eli, c_date = get_latest_labor_code_automated()
        
        if not url:
            return {"status": "skipped", "message": "Posiadasz już najnowszą wersję lub błąd ISAP"}

        # 3. Pobranie pliku PDF
        success_download = download_specific_unified_text(target_eli=eli, pdf_url=url)
        
        if success_download:
            # 4. PRZETWARZANIE DO QDRANT
            print("--- URUCHAMIANIE INGESTION DO QDRANT CLOUD ---")
            run_ingestion(status_date=c_date) ### wywołuje skrypt ingest_to_cloud.py
            #### przekazuje zmienną c_date, którą funkcja get_latest_labor_code_automated() wyciągnęła wcześniej z ISAP
            
            # zapisuje metadane dopiero gdy proces (pobranie + Qdrant) się uda
            save_metadata(eli, c_date)
            
            # 5. Odświeżenie silnika RAG (żeby widział nowe dane bez restartu serwera)
            global rag_engine
            rag_engine = LaborLawRAG()
            
            return {
                "status": "success",
                "message": f"Zaktualizowano bazę wiedzy do wersji: {eli}",
                "details": "Pobrano PDF i zaktualizowano kolekcję w Qdrant Cloud.",
                "updated_at": c_date
            }
        
        return {"status": "error", "message": "Błąd podczas pobierania PDF"}

    except Exception as e:
        print(f"BŁĄD PODCZAS AKTUALIZACJI: {str(e)}")
        raise HTTPException(status_code=500, detail=str(e))


# ENDPOINT Z LOGOWANIEM
@app.post("/ask")
async def ask_lawyer(query: Query, db: Session = Depends(get_db)):
    try:
        print(f"--- NOWE ZAPYTANIE OD: {query.session_id} ---")
        
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


# PROXY DLA TELEGRAMA (omija blokady Hugging Face)
@app.api_route("/tg-proxy/{path:path}", methods=["GET", "POST"])
async def tg_proxy(path: str, request: Request):
    # Buduje adres do prawdziwego API Telegrama
    url = f"https://api.telegram.org/{path}"
    
    # Pobiera body i parametry z n8n
    body = await request.body()
    params = dict(request.query_params)
    
    # Przesyła zapytanie do Telegrama
    # Odfiltrowuje nagłówek 'host', żeby Telegram się nie pomylił
    headers = {k: v for k, v in request.headers.items() if k.lower() != 'host'}
    
    response = requests.request(
        method=request.method,
        url=url,
        params=params,
        data=body,
        headers=headers,
        timeout=10
    )
    
    # Zwraca odpowiedź z Telegrama prosto do n8n
    return Response(
        content=response.content,
        status_code=response.status_code,
        headers=dict(response.headers)
    )


# ENDPOINT DLA BACKENDU C# (BEZ LOGOWANIA DO BAZY)
# Ten endpoint jest "bezstanowy" - C# przesyła pytanie a Python tylko odpowiada
@app.post("/api/v1/legal-brain/ask")
async def ask_legal_brain(query: Query):
    try:
        ## wywołuje silnik RAG bez pobierania historii z bazy Pythona
        ## jeśli C# będzie chciał uwzględnić historię prześle ją w pytaniu
        result = rag_engine.ask(query.question, chat_history=[]) 
        
        return {
            "answer": result["answer"],
            "sources": result["sources"]
        }
    except Exception as e:
        print(f"BŁĄD LEGAL-BRAIN: {str(e)}")
        raise HTTPException(status_code=500, detail=str(e))


@app.get("/health")
async def health_check():
    return {"status": "ok", "database": "connected"}


if __name__ == "__main__":
    # Render podaje port w zmiennej środowiskowej PORT
    port = int(os.environ.get("PORT", 8000))
    uvicorn.run(app, host="0.0.0.0", port=port)