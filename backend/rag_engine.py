import os
from dotenv import load_dotenv
import requests
import time
from groq import Groq
from qdrant_client import QdrantClient, models


load_dotenv()


class LaborLawRAG:
    def __init__(self, collection_name="labor_code_pl"):
        ## Połączenie z bazą (Qdrant Cloud)
        self.client = QdrantClient(url=os.getenv("QDRANT_URL"), api_key=os.getenv("QDRANT_API_KEY"))
        self.collection_name = collection_name
        
        ## LLM
        self.groq_api_key = os.getenv("GROQ_API_KEY")
        self.groq = Groq(api_key=self.groq_api_key)

        ## Hugging face
        self.hf_token = os.getenv("HF_API_KEY")
        ### URL-e do modeli na Hugging Face
        self.embed_url = "https://router.huggingface.co/hf-inference/models/intfloat/multilingual-e5-large/pipeline/feature-extraction"
        self.rerank_url = "https://router.huggingface.co/hf-inference/models/BAAI/bge-reranker-v2-m3"

    def query_hf(self, url, payload):
        headers = {"Authorization": f"Bearer {self.hf_token}"}
        response = requests.post(url, headers=headers, json=payload)

        ### jeśli model się ładuje (503) czeka i ponawia
        if response.status_code == 503:
            wait_time = response.json().get("estimated_time", 20)
            print(f"Model się ładuje, czekam {wait_time}s...")
            time.sleep(wait_time)
            return self.query_hf(url, payload)

        if response.status_code != 200:
            raise Exception(f"Błąd Hugging Face ({response.status_code}): {response.text}")

        return response.json()

    def get_context(self, query, limit=20):
        # 1. Generowanie wektora przez HF API (Dense)
        ### Model E5 wymaga przedrostka "query: " dla pytań
        hf_resp = self.query_hf(self.embed_url, {"inputs": f"query: {query}"})

        ### HF często zwraca listę list [[...]] -> wyciąganie pierwszego wektora
        if isinstance(hf_resp, list) and isinstance(hf_resp[0], list):
            dense_vec = hf_resp[0]
        elif isinstance(hf_resp, list):
            dense_vec = hf_resp
        else:
            raise Exception(f"Nieoczekiwany format wektora z HF: {hf_resp}")


        # 2. Wyszukiwanie w Qdrant (Tylko Dense bo Sparse przez API jest trudne)
        results = self.client.search(
            collection_name=self.collection_name,
            query_vector=dense_vec,
            limit=limit,
            with_payload=True
        )


        # 3. Reranking (Cross-Encoder) przez HF API
        if results:
            #### Poprawny format dla BGE Reranker na HF Inference API
            payload = {
                "inputs": [
                    {"text": query, "text_pair": res.payload.get('content', '')}
                    for res in results
                ]
            }
            rerank_resp = self.query_hf(self.rerank_url, payload)

            ## HF zwraca [{'label': 'LABEL_0', 'score': 0.99}, ...]
            ### sortowanie wyników Qdrant na podstawie wyników z HF
            try:
                # Mapuje wyniki: HF zwraca wyniki w tej samej kolejności co wysłane pary
                scored_results = []
                for i, r in enumerate(rerank_resp):
                    score = r.get('score', 0)
                    scored_results.append((score, results[i]))

                scored_results.sort(key=lambda x: x[0], reverse=True)
                results = [item[1] for item in scored_results]
            except Exception as e:
                print(f"Reranking nieudany, używam kolejności z Qdrant. Błąd: {e}")


        # 4. Formatowanie wyników - Lejek (Top 10)

        context_parts = []
        
        sources = [] # list zamiast set, aby zachować KOLEJNOŚĆ

        ## reranker widział 20, ale do LLM-a wyśle tylko top 10 aby wziąć tylko najlepsze
        for res in results[:10]:
            art_id = res.payload.get('metadata', {}).get('art_id', 'Nieznany')
            content = res.payload.get('content', '')
            context_parts.append(f"[{art_id}]: {content}")

            # dodaje do źródeł tylko jeśli jeszcze go nie ma (deduplikacja), ale NIE SORTUJE na końcu!
            if art_id not in sources:
                sources.append(art_id)

        # zwraca sources bez funkcji sorted()
        return "\n\n".join(context_parts), sources

    def rewrite_query(self, question, chat_history):
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
        
        res = self.groq.chat.completions.create(
            messages=[{"role": "user", "content": prompt}],
            model="llama-3.3-70b-versatile",
            temperature=0
        )
        return res.choices[0].message.content

    def ask(self, question, chat_history=None):
        # przepisywanie zapytania jeśli jest historia --> szukanie w Qdrancie za pomocą "mądrzejszego" pytania
        search_query = self.rewrite_query(question, chat_history) if chat_history else question

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
        
        ## wysłanie całej listy do Groq
        chat = self.groq.chat.completions.create(
            messages=messages,
            model="llama-3.3-70b-versatile",
            temperature=0.1 ### aby odpowiedzi były maksymalnie precyzyjne i mało kreatywne
        )

        return {
            "answer": chat.choices[0].message.content,
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
