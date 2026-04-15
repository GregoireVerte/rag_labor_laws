import requests
import os
import time
from dotenv import load_dotenv

load_dotenv()

# Konfiguracja z .env
HF_TOKEN = os.getenv("HF_API_KEY")

def query_hf_api(url, payload):
    """
    Silnik zapytań:
    - Obsługuje nagłówki
    - Obsługuje błąd 503 (ładowanie modelu)
    - Obsługuje błędy sieciowe (timeout)
    - Posiada pełny blok try-except
    """
    headers = {"Authorization": f"Bearer {HF_TOKEN}"}
    
    try:
        # Ustawia timeout na 60s bo modele potrafią długo myśleć
        response = requests.post(url, headers=headers, json=payload, timeout=60)

        # Obsługa ładowania modelu (503)
        if response.status_code == 503:
            wait_time = response.json().get("estimated_time", 20)
            print(f"Model HF ({url.split('/')[-1]}) się ładuje, czekam {wait_time}s...")
            time.sleep(wait_time)
            return query_hf_api(url, payload) ### Rekurencyjne ponowienie

        if response.status_code != 200:
            raise Exception(f"Błąd HF API ({response.status_code}): {response.text}")

        return response.json()

    except requests.exceptions.RequestException as e:
        print(f"Błąd sieciowy podczas zapytania do HF: {e}")
        raise
    except Exception as e:
        print(f"Nieoczekiwany błąd utils.query_hf_api: {e}")
        raise

def get_embeddings(texts, is_query=True):
    """
    Wrapper dla modelu E5:
    - Przygotowuje prefixy 'query:'/'passage:'
    - Obsługuje zamianę pojedynczego stringa na listę
    - Wywołuje silnik query_hf_api
    """
    url = "https://router.huggingface.co/hf-inference/models/intfloat/multilingual-e5-large/pipeline/feature-extraction"
    
    # Model E5 wymaga przedrostka
    prefix = "query: " if is_query else "passage: "
    
    # Normalizacja wejścia do listy
    input_texts = [texts] if isinstance(texts, str) else texts
    formatted_inputs = [f"{prefix}{t}" for t in input_texts]

    payload = {
        "inputs": formatted_inputs,
        "options": {"wait_for_model": True}
    }

    # Wywołuje funkcję z całą logiką try-except i nagłówkami
    return query_hf_api(url, payload)