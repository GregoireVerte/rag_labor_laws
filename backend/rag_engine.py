import os
from dotenv import load_dotenv
from groq import Groq
from qdrant_client import QdrantClient
from sentence_transformers import SentenceTransformer


load_dotenv()


class LaborLawRAG:
    def __init__(self, collection_name="labor_code_pl"):
        self.client = QdrantClient(host="localhost", port=6333)
        self.model = SentenceTransformer('sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2', device='cuda')
        self.groq = Groq(api_key=os.environ.get("GROQ_API_KEY"))
        self.collection_name = collection_name

    def get_context(self, query, limit=15):
        query_vector = self.model.encode(query).tolist()
        results = self.client.query_points(
            collection_name=self.collection_name,
            query=query_vector,
            limit=limit,
            with_payload=True
        ).points

        context_parts = []
        sources = set() #### set() żeby uniknąć duplikatów numerów artykułów

        for res in results:
            art_id = res.payload.get('art_id', 'Nieznany')
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
