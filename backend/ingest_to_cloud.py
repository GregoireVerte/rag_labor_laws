import os
import re
import uuid
import time
import random
from dotenv import load_dotenv
from qdrant_client import QdrantClient
from qdrant_client.http import models
from qdrant_client.http.models import PointStruct, CreateAliasOperation, DeleteAliasOperation, AliasOperations
from langchain_community.document_loaders import PyPDFLoader

from utils import get_embeddings

# Ładowanie konfiguracji
load_dotenv()
QDRANT_URL = os.getenv("QDRANT_URL")
QDRANT_API_KEY = os.getenv("QDRANT_API_KEY")
ALIAS_NAME = "labor_code_pl"

# Inicjalizacja Klienta (Chmura)
client = QdrantClient(url=QDRANT_URL, api_key=QDRANT_API_KEY)

def run_ingestion(status_date="2026-02-03"):
    print(f"Rozpoczynam bezpieczną migrację danych do Qdrant Cloud: {QDRANT_URL}")

    # Przygotowanie Kolekcji 
    ## Tworzy unikalną nazwę dla nowej kolekcji (np. z timestampem)
    temp_collection_name = f"labor_code_{int(time.time())}"

    ### Tworzenie TYMCZASOWEJ Kolekcji
    client.create_collection(
        collection_name=temp_collection_name,
        vectors_config=models.VectorParams(
            size=1024, # Dla intfloat/multilingual-e5-large
            distance=models.Distance.COSINE
        )
    )

    # Wczytywanie i przetwarzanie PDF
    file_path = "last_unified_labor_code.pdf" # Zakłada że plik jest w tym samym folderze
    if not os.path.exists(file_path):
        print(f"Błąd: Nie znaleziono pliku {file_path}")
        client.delete_collection(collection_name=temp_collection_name)
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

    # Generowanie Embeddingów (Paczki z pełnym przechwytywaniem błędów)
    print("Generowanie wektorów przez HF API (Dense)...")
    dense_embeddings = []
    batch_size = 20 ### bezpieczna wielkość paczki dla HF API

    try:
        for i in range(0, len(articles), batch_size):
            batch = articles[i:i + batch_size]
            print(f"Przetwarzanie paczki {i//batch_size + 1}/{(len(articles) + batch_size - 1)//batch_size}...")

            ### Wywołanie funkcji z utils (is_query=False bo to dokumenty)
            batch_embeddings = get_embeddings(batch, is_query=False)
            dense_embeddings.extend(batch_embeddings)

            ### mała przerwa aby nie spamować HF API zbyt szybko
            time.sleep(random.uniform(0.8, 1.5))

    except Exception as e:
        print(f"❌ BŁĄD PODCZAS GENEROWANIA EMBEDDINGÓW: {e}")
        print("Anulowanie aktualizacji! Stara baza produkcyjna pozostaje NIENARUSZONA.")
        client.delete_collection(collection_name=temp_collection_name)
        raise e  # Rzuca błąd dalej, aby FastAPI/n8n wiedziało o porażce

    # Budowanie punktów i wysyłka do tymczasowej kolekcji
    points = []
    for i, content in enumerate(articles):
        # Wyciąganie numeru artykułu do metadanych
        match = re.search(r"Art\.\s+(\d+[a-z]*)", content)
        art_id = f"Art. {match.group(1)}" if match else "Wstęp"

        points.append(
            PointStruct(
                id=str(uuid.uuid4()),
                vector=dense_embeddings[i], ### teraz to po prostu lista (wektor)
                payload={
                    "content": content,
                    "metadata": {
                        "art_id": art_id,
                        "source": "Kodeks Pracy",
                        "status_date": status_date
                    }
                }
            )
        )

    print(f"Wgrywanie {len(points)} punktów do kolekcji tymczasowej {temp_collection_name}...")
    client.upsert(collection_name=temp_collection_name, points=points)

    # BEZPRZESTOJOWA ZMIANA ALIASU (Atomowa podmiana)
    print("Przeprowadzenie atomowej podmiany kolekcji w produkcji...")

    # Sprawdza czy ALIAS_NAME już istnieje jako alias
    existing_aliases = client.get_aliases().aliases
    alias_exists = any(a.alias_name == ALIAS_NAME for a in existing_aliases)
    old_collections_to_delete = [
        a.collection_name for a in existing_aliases if a.alias_name == ALIAS_NAME
    ]

    # Tylko dla PIERWSZEJ migracji: usuwa starą ZWYKŁĄ kolekcję (o ile nie jest jeszcze aliasem)
    if not alias_exists and client.collection_exists(ALIAS_NAME):
        client.delete_collection(collection_name=ALIAS_NAME)

    # Przygotowanie atomowej operacji - zmiana/tworzenie aliasu
    alias_operations = []
    if alias_exists:
        # Jeśli alias już istnieje, musi go najpierw usunąć w tej samej transakcji
        alias_operations.append(
            DeleteAliasOperation(
                delete_alias=models.DeleteAlias(alias_name=ALIAS_NAME)
            )
        )
    
    alias_operations.append(
        CreateAliasOperation(
            create_alias=models.CreateAlias(
                collection_name=temp_collection_name,
                alias_name=ALIAS_NAME
            )
        )
    )

    # Wykonanie atomowej podmiany w Qdrant
    client.update_collection_aliases(
        change_aliases_operations=[AliasOperations(action=op) for op in alias_operations]
    )

    ## Sprzątanie starych kolekcji po udanej podmianie
    for old_coll in old_collections_to_delete:
        if old_coll != temp_collection_name:
            client.delete_collection(collection_name=old_coll)

    print("✅ Sukces! Baza wiedzy (wektorowa) została zaktualizowana bez ani jednej milisekundy przerwy w działaniu bota.")

if __name__ == "__main__":
    run_ingestion()