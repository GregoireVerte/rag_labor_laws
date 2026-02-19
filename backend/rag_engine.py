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
        return "\n\n".join([f"[{res.payload.get('art_id')}]: {res.payload.get('content')}" for res in results])

    def ask(self, question):
        context = self.get_context(question)
        # Budowanie System Promptu
        system_prompt = f"""Jesteś pomocnym i precyzyjnym asystentem oraz ekspertem od polskiego prawa pracy. 
        Zawsze odpowiadaj tylko na podstawie dostarczonego kontekstu w postaci artykułów ustawy / rozporządzeń bez używania wcześniejszej wiedzy ogólnej. 
        Jeśli odpowiedzi nie ma w kontekście, poinformuj o tym. 
        Odpowiedź musi być w języku polskim, chyba że użytkownik wyraźnie zaznaczy, że ma być w innym konkretnym języku (np. angielskim).
        WAŻNE: Używaj wyłącznie alfabetu łacińskiego. Nie używaj cyrylicy ani znaków azjatyckich.

        KONTEKST:
        {context}"""
        
        chat = self.groq.chat.completions.create(
            messages=[{"role": "system", "content": system_prompt}, {"role": "user", "content": question}],
            model="llama-3.3-70b-versatile",
            temperature=0.1 ### aby odpowiedzi były maksymalnie precyzyjne i mało kreatywne
        )
        return chat.choices[0].message.content
    

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
