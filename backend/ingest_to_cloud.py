import os
import re
import uuid
from dotenv import load_dotenv
from qdrant_client import QdrantClient
from qdrant_client.http import models
from qdrant_client.http.models import PointStruct
from langchain_community.document_loaders import PyPDFLoader
from fastembed import TextEmbedding, SparseTextEmbedding

# 1. Ładowanie konfiguracji
load_dotenv()
QDRANT_URL = os.getenv("QDRANT_URL")
QDRANT_API_KEY = os.getenv("QDRANT_API_KEY")
COLLECTION_NAME = "labor_code_pl"

# 2. Inicjalizacja Klienta (Chmura)
client = QdrantClient(url=QDRANT_URL, api_key=QDRANT_API_KEY)

def run_ingestion():
    print(f"Rozpoczynam migrację danych do Qdrant Cloud: {QDRANT_URL}")

    # --- KROK 1: Przygotowanie Kolekcji ---
    # Usuwa starą (jeśli istnieje) i tworzy nową z obsługą Hybrydy
    client.delete_collection(collection_name=COLLECTION_NAME)
    client.create_collection(
        collection_name=COLLECTION_NAME,
        vectors_config=models.VectorParams(
            size=1024, # Dla intfloat/multilingual-e5-large
            distance=models.Distance.COSINE
        ),
        sparse_vectors_config={
            "text-sparse": models.SparseVectorParams()
        }
    )

    # --- KROK 2: Wczytywanie i przetwarzanie PDF ---
    file_path = "last_unified_labor_code.pdf" # Zakłada że plik jest w tym samym folderze
    if not os.path.exists(file_path):
        print(f"Błąd: Nie znaleziono pliku {file_path}")
        return

    loader = PyPDFLoader(file_path)
    pages = loader.load()
    
    full_text = ""
    for page in pages:
        content = page.page_content
        content = re.sub(r"©Kancelaria Sejmu.*s\.\s\d+/\d+", "", content)
        content = re.sub(r"2026-02-03", "", content) 
        full_text += content + "\n"

    # Podział na artykuły
    pattern = r"(?=Art\.\s+\d+[a-z]*\.)"
    articles = [c.strip() for c in re.split(pattern, full_text) if c.strip()]
    print(f"Przygotowano {len(articles)} artykułów do zakodowania.")

    # --- KROK 3: Generowanie Embeddingów ---
    print("Generowanie wektorów (Dense & Sparse)...")
    dense_model = TextEmbedding(model_name="intfloat/multilingual-e5-large")
    sparse_model = SparseTextEmbedding(model_name="Qdrant/bm25")

    dense_embeddings = list(dense_model.embed(articles))
    sparse_embeddings = list(sparse_model.embed(articles))

    # --- KROK 4: Budowanie punktów i wysyłka ---
    points = []
    for i, content in enumerate(articles):
        # Wyciąganie numeru artykułu do metadanych
        match = re.search(r"Art\.\s+(\d+[a-z]*)", content)
        art_id = f"Art. {match.group(1)}" if match else "Wstęp"

        points.append(
            PointStruct(
                id=str(uuid.uuid4()),
                vector={
                    "": dense_embeddings[i].tolist(),
                    "text-sparse": sparse_embeddings[i].as_object()
                },
                payload={
                    "content": content,
                    "metadata": {
                        "art_id": art_id,
                        "source": "Kodeks Pracy",
                        "status_date": "2026-02-03"
                    }
                }
            )
        )

    print(f"Wgrywanie {len(points)} punktów do chmury...")
    client.upsert(collection_name=COLLECTION_NAME, points=points)
    print("Dane są już w Qdrant Cloud.")

if __name__ == "__main__":
    run_ingestion()