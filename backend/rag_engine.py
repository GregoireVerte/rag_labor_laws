import os
import time
import random
import re
from dotenv import load_dotenv
from groq import Groq
from openai import OpenAI
from qdrant_client import QdrantClient
from utils import get_embeddings, query_hf_api


load_dotenv()


class LaborLawRAG:
    def __init__(self, collection_name="labor_code_pl"):
        ## Połączenie z bazą (Qdrant Cloud)
        self.client = QdrantClient(url=os.getenv("QDRANT_URL"), api_key=os.getenv("QDRANT_API_KEY"))
        self.collection_name = collection_name
        
        ## LLM
        self.groq_api_key = os.getenv("GROQ_API_KEY")
        self.groq = Groq(api_key=self.groq_api_key)

        ### URL do modeli na Hugging Face
        self.rerank_url = "https://router.huggingface.co/hf-inference/models/BAAI/bge-reranker-v2-m3"

    def _get_llm_client(self, custom_api_key=None, provider=None):
        if custom_api_key and custom_api_key.strip():
            prov = (provider or "openrouter").lower()
            if prov == "openrouter":
                ### OpenRouter (z darmowym ruterem modeli (:free)) - używa standardu OpenAI
                return OpenAI(base_url="https://openrouter.ai/api/v1", api_key=custom_api_key.strip()), "openrouter/free"
            elif prov == "google":
                ### Stabilny i darmowy Gemini 2.0 Flash z Google AI Studio - jest w standardzie OpenAI
                return OpenAI(base_url="https://generativelanguage.googleapis.com/v1beta/openai/", api_key=custom_api_key.strip()), "gemini-2.0-flash"
            elif prov == "groq":
                return Groq(api_key=custom_api_key.strip()), "qwen/qwen3.6-27b"

        ## Domyślny fallback systemowy (Groq)
        return self.groq, "qwen/qwen3.6-27b"

    def _clean_think_tags(self, text: str) -> str:
        if not text:
            return ""
        ## Wyczyszczenie <think>...</think> lub nieobsłużonego <think> do końca tekstu (wraz ze znakami nowej linii)
        cleaned_text = re.sub(r'<think>(?:.*?</think>|.*)', '', text, flags=re.DOTALL)
        return cleaned_text.strip()

    def get_context(self, query, limit=50):
        dense_vec = None
        
        # 1. Generowanie wektora przez utils - HF API (Dense)
        #### Model E5 wymaga przedrostka 'query: ' lub 'passage: ' dla pytań
        #### Generowanie wektora z mechanizmem Retry (3 próby w razie Timeoutu)
        for attempt in range(3):
            try:
                hf_resp = get_embeddings(query, is_query=True)

                ### HF API dla feature-extraction zwraca zazwyczaj [[wektor]]
                ### HF często zwraca listę list [[...]] -> wyciąganie pierwszego wektora
                if isinstance(hf_resp, list) and isinstance(hf_resp[0], list):
                    dense_vec = hf_resp[0]
                elif isinstance(hf_resp, list):
                    dense_vec = hf_resp
                else:
                    raise Exception(f"Nieoczekiwany format wektora z HF: {hf_resp}")
                break  # Sukces - przerywa pętlę retry
            except Exception as e:
                print(f"[Embedding Próba {attempt+1}/3 Nieudana]: {e}")
                if attempt == 2:  ### Ostatnia próba zawiodła
                    raise e
                time.sleep(random.uniform(2, 4))  ## Poczeka 2 - 4 sekundy przed kolejną próbą


        # 2. Wyszukiwanie w Qdrant Cloud (Tylko Dense bo Sparse przez API jest trudne)
        results = self.client.search(
            collection_name=self.collection_name,
            query_vector=dense_vec,
            limit=limit,
            with_payload=True
        )


        # 3. Reranking (Cross-Encoder) przez HF API - z automatycznym Retry i bezpiecznym parsowaniem struktury
        if results:
            #### format słownikowy dla Hugging Face Inference API (obsługa par tekstowych)
            payload = {
                "inputs": [
                    {"text": query, "text_pair": res.payload.get('content', '')}
                    for res in results
                ]
            }

            rerank_resp = None
            for attempt in range(3):
                try:
                    rerank_resp = query_hf_api(self.rerank_url, payload) ## użycie wspólnej funkcji z utils
                    break
                except Exception as e:
                    print(f"[Reranker Próba {attempt+1}/3 Nieudana]: {e}")
                    if attempt < 2:
                        time.sleep(random.uniform(2, 4))

            ### DIAGNOSTYKA (wyłączona żeby nie zaśmiecać logów produkcyjnych)
            ### print(f"🔍 [DEBUG RERANKER] Typ: {type(rerank_resp)} | Zawartość: {str(rerank_resp)[:500]}")

            # Jeśli Hugging Face przysłał zagnieżdżoną listę [[ ... ]], wyciąga jej środek:
            if rerank_resp and isinstance(rerank_resp, list) and len(rerank_resp) > 0 and isinstance(rerank_resp[0], list):
                rerank_resp = rerank_resp[0]

            ## ZABEZPIECZENIE: sprawdza czy liczba punktów z HF zgadza się z Qdrant
            if rerank_resp and isinstance(rerank_resp, list) and len(rerank_resp) == len(results):
                try:
                    ### HF zwraca [{'label': 'LABEL_0', 'score': 0.99}, ...]
                    ### sortowanie wyników Qdrant na podstawie wyników z HF
                    ### Mapuje wyniki: HF zwraca wyniki w tej samej kolejności co wysłane pary
                    scored_results = []
                    for i, r in enumerate(rerank_resp):
                        ## Bezpieczne wyciąganie score niezależnie czy HF zwrócił słownik, czy listę słowników
                        if isinstance(r, dict):
                            score = r.get('score', 0)
                        elif isinstance(r, list) and len(r) > 0 and isinstance(r[0], dict):
                            score = r[0].get('score', 0)
                        elif isinstance(r, (int, float)):
                            score = r
                        else:
                            score = 0

                        scored_results.append((score, results[i]))

                    scored_results.sort(key=lambda x: x[0], reverse=True)
                    results = [item[1] for item in scored_results]
                    print("--- RERANKING ZAKOŃCZONY SUKCESEM ---")
                except Exception as e:
                    print(f"Błąd parsowania odpowiedzi rerankera, używam kolejności z Qdrant. Szczegóły: {e}")
            else:
                print("--- [WARNING] Reranker zwrócił niezgodną liczbę wyników. Bezpieczny fallback do kolejności z Qdrant! ---")


        # 4. Formatowanie wyników - Lejek (Top 15)

        context_parts = []
        
        sources = [] # list zamiast set, aby zachować KOLEJNOŚĆ

        ## reranker widział 50, ale do LLM-a wyśle tylko top 15 aby wziąć tylko najlepsze
        for res in results[:15]:
            art_id = res.payload.get('metadata', {}).get('art_id', 'Nieznany')
            content = res.payload.get('content', '')
            context_parts.append(f"[{art_id}]: {content}")

            # dodaje do źródeł tylko jeśli jeszcze go nie ma (deduplikacja), ale NIE SORTUJE na końcu!
            if art_id not in sources:
                sources.append(art_id)

        # zwraca sources bez funkcji sorted()
        return "\n\n".join(context_parts), sources

    def rewrite_query(self, question, chat_history, llm_client, model_name):
        if not chat_history:
            return question
            
        # Prosi AI o stworzenie zapytania wyszukiwarkowego na podstawie wcześniejszej historii
        history_text = "\n".join([f"User: {q}\nAI: {a}" for q, a in chat_history])
        
        prompt = f"""Na podstawie poniższej historii rozmowy oraz nowego pytania, stwórz jedno samodzielne i precyzyjne zapytanie do bazy dokumentów prawnych. 
        Zapytanie musi zawierać wszystkie niezbędne słowa kluczowe (np. temat rozmowy), aby wyszukiwarka znalazła właściwy artykuł.
        
        HISTORIA:
        {history_text}
        
        NOWE PYTANIE: {question}
        
        SAMODZIELNE ZAPYTANIE:"""
        
        res = llm_client.chat.completions.create(
            messages=[{"role": "user", "content": prompt}],
            model=model_name,
            temperature=0
        )
        raw_content = res.choices[0].message.content
        return self._clean_think_tags(raw_content)

    def ask(self, question, chat_history=None, custom_api_key=None, provider=None):

        # Pobranie odpowiedniego klienta LLM (własny klucz lub domyślny Groq)
        llm_client, model_name = self._get_llm_client(custom_api_key, provider)

        # przepisywanie zapytania z użyciem dobranego LLM, jeśli jest historia --> szukanie w Qdrancie za pomocą "mądrzejszego" pytania
        search_query = self.rewrite_query(question, chat_history, llm_client, model_name) if chat_history else question

        # pobieranie kontekstu na podstawie "mądrzejszego" zapytania jeśli jest historia
        context, sources = self.get_context(search_query) ## pobieranie kontekstu i listy źródeł

        ## Budowanie System Promptu ## inicjowanie listy wiadomości od instrukcji systemowej
        messages = [
            {
                "role": "system",
                "content": f"""Jesteś pomocnym i precyzyjnym asystentem oraz ekspertem od polskiego prawa pracy. 
                Zawsze odpowiadaj tylko na podstawie dostarczonego kontekstu w postaci artykułów ustawy / rozporządzeń bez używania wcześniejszej wiedzy ogólnej. 
                Jeśli odpowiedzi nie ma w kontekście, poinformuj o tym. 
                Odpowiedź musi być w języku polskim, chyba że użytkownik wyraźnie zaznaczy, że ma być w innym konkretnym języku (np. angielskim).
                WAŻNE: Używaj wyłącznie alfabetu łacińskiego. Nie używaj cyrylicy ani znaków azjatyckich.
                Zawsze wskazuj podstawę prawną (numer artykułu) dla każdej podanej informacji, np. [Art. 100].
                Jeśli artykuły zawierają terminy, podawaj ich definicje, jeśli są obecne w kontekście.
                Formatuj odpowiedzi w sposób przejrzysty: używaj punktów i pogrubień dla kluczowych terminów prawnych.
                Nigdy nie interpretuj przepisów w sposób wykraczający poza brzmienie dostarczonego tekstu.
                Jeśli kontekst zawiera sprzeczne informacje, wskaż obie i zaznacz, że przepisy mogą być interpretowane wieloznacznie.

                KONTEKST:
                {context}"""
            }
        ]

        ## jeśli otrzymano historię to następuje dodanie jej do listy wiadomości
        ## założenie że chat_history to lista krotek: [(pytanie1, odpowiedź1), (pytanie2, odpowiedź2)]
        if chat_history:
            for old_question, old_answer in chat_history:
                messages.append({"role": "user", "content": old_question})
                messages.append({"role": "assistant", "content": old_answer})

        ## na końcu dodanie bieżącego pytania użytkownika
        messages.append({"role": "user", "content": question})
        
        ## wysłanie całej listy do wywołanego dobranego klienta LLM (domyślnie Groq)
        chat = llm_client.chat.completions.create(
            messages=messages,
            model=model_name,
            temperature=0.1, ### aby odpowiedzi były maksymalnie precyzyjne i mało kreatywne
            max_tokens=1024 ## <-- Zapewnia odpowiedni bufor na pełną odpowiedź (bezpieczny chroniący przed błędem 413 / TPM Limit na Groq)
        )

        raw_content = chat.choices[0].message.content
        clean_answer = self._clean_think_tags(raw_content)

        return {
            "answer": clean_answer,
            "sources": sources
        }
    

if __name__ == "__main__":
    # test działania klasy bezpośrednio z terminala
    print("Test silnika RAG...")
    try:
        rag = LaborLawRAG()
        pytanie = "Ile dni urlopu ma pracownik po 15 latach pracy?"
    
        odp = rag.ask(pytanie)
        print(f"\nPYTANIE: {pytanie}")
        print(f"ODPOWIEDŹ:\n{odp}")
    except Exception as e:
        print(f"Błąd podczas testu: {e}")
