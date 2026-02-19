from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from rag_engine import LaborLawRAG
import uvicorn

# 1. Inicjalizacja aplikacji i silnika RAG
app = FastAPI(title="Labor Law RAG API")
rag_engine = LaborLawRAG()

# 2. Definicja struktury zapytania (to co wysyła użytkownik)
class Query(BaseModel):
    question: str

# 3. Endpoint do zadawania pytań
@app.post("/ask")
async def ask_lawyer(query: Query):
    try:
        answer = rag_engine.ask(query.question)
        return {"question": query.question, "answer": answer}
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

# 4. Prosty testowy endpoint
@app.get("/health")
async def health_check():
    return {"status": "ok", "database": "connected"}

if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=8000)