import os
from dotenv import load_dotenv
from groq import Groq
from qdrant_client import QdrantClient, models
from fastembed import TextEmbedding, SparseTextEmbedding
from sentence_transformers import CrossEncoder


load_dotenv()


class LaborLawRAG:
    def __init__(self, collection_name="labor_code_pl"):
        ## 1. Połączenie z bazą
        self.client = QdrantClient(host="localhost", port=6333, check_compatibility=False)
        self.collection_name = collection_name

        ## 2. LLM
        self.groq = Groq(api_key=os.environ.get("GROQ_API_KEY"))

        ## 3. Modele do Hybrid Search (zamiast starego SentenceTransformer)
        self.dense_model = TextEmbedding(model_name="intfloat/multilingual-e5-large")
        self.sparse_model = SparseTextEmbedding(model_name="Qdrant/bm25")

        ## 4. Parametr Alpha (balans między sensem a słowem kluczowym)
        self.alpha = 0.7

        ## 5. Reranker (Sędzia - Cross-Encoder) ### Model, który bardzo dokładnie porównuje parę (pytanie, artykuł)
        self.reranker = CrossEncoder('cross-encoder/ms-marco-MiniLM-L12-v2')

    def get_context(self, query, limit=20):
        # 1. Generowanie wektorów z pytania
        dense_vec = list(self.dense_model.embed([query]))[0].tolist()
        sparse_vec = list(self.sparse_model.embed([query]))[0]

        ## Ręczne skalowanie wagą alpha # mnożenie każdej wartości w wektorze przez wagę
        query_dense_scaled = [v * self.alpha for v in dense_vec]

        ## dla sparse mnożenie wartości w słowniku (indices pozostają bez zmian)
        query_sparse_scaled = sparse_vec
        for i in range(len(query_sparse_scaled.values)):
            query_sparse_scaled.values[i] *= (1.0 - self.alpha)

        # 2. Hybrydowe wyszukiwanie (Hybrid Search)
        ### DBS (Distribution Based Score)
        results = self.client.query_points(
            collection_name=self.collection_name,
            prefetch=[
                models.Prefetch(query=query_dense_scaled, using="", limit=limit), # szuka po sensie
                models.Prefetch(query=query_sparse_scaled.as_object(), using="text-sparse", limit=limit), # szuka po słowach
            ],
            ## użycie dbsf bez wag (bo wagi są już w wektorach)
            query=models.FusionQuery(fusion="dbsf"),
            limit=limit,
            with_payload=True
        ).points

        # 3. Reranking (Cross-Encoder)
        if results:
            ### Przygotowuje pary (pytanie, treść artykułu) do oceny
            pairs = [[query, res.payload.get('content', '')] for res in results]
            rerank_scores = self.reranker.predict(pairs)

            ### Łączy wyniki z nowymi punktami w listę krotek (score, point)
            scored_results = list(zip(rerank_scores, results))

            ### Sortuje po nowym score (indeks 0) od najwyższego
            scored_results.sort(key=lambda x: x[0], reverse=True)

            ### Wyciąga same punkty w nowej kolejności
            results = [item[1] for item in scored_results]

            print(f"DEBUG: Reranked {len(results)} items")

        # 4. Formatowanie wyników (już posortowanych przez Rerankera)

        context_parts = []
        
        sources = set() #### set() żeby uniknąć duplikatów numerów artykułów

        for res in results:
            art_id = res.payload.get('metadata', {}).get('art_id', 'Nieznany')
            content = res.payload.get('content', '')
            context_parts.append(f"[{art_id}]: {content}")
            sources.add(art_id)

        return "\n\n".join(context_parts), sorted(list(sources))

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
