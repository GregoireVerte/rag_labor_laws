import requests
import os
from langchain_community.document_loaders import PyPDFLoader

def download_specific_unified_text():
    # konkretny ID ostatniego dużego tekstu jednolitego
    target_eli = "DU/2025/277"
    
    # link bezpośredni do PDF tego konkretnego aktu
    # oficjalny PDF z Dziennika Ustaw (nie generowany, tylko skan/cyfrowy)
    pdf_url = "https://isap.sejm.gov.pl/isap.nsf/download.xsp/WDU20250000277/U/D20250277Lj.pdf"
    file_path = "kodeks_pracy_2025_unified.pdf"

    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36"
    }

    print(f"Tekst jednolity Kodeksu pracy ({target_eli})")
    print(f"Pobieranie z: {pdf_url}...")
    
    try:
        response = requests.get(pdf_url, headers=headers, timeout=30)
        
        # Diagnostyka: jeśli to nie jest PDF (200)
        if response.status_code == 200:
            with open(file_path, 'wb') as f:
                f.write(response.content)
            print(f"Plik zapisany jako: {file_path}")
            
            # Test odczytu LangChain
            print("\nSprawdzenie czy LangChain widzi tekst...")
            try:
                loader = PyPDFLoader(file_path)
                pages = loader.load()
                print(f"   Liczba stron: {len(pages)}")
                
                if len(pages) > 2:
                    print("\n--- PRÓBKA (Strona 3) ---")
                    # wyświetlany fragment żeby potwierdzić polskie znaki
                    print(pages[2].page_content[:600]) 
                    print("-------------------------")
            except Exception as e:
                print(f"Plik pobrany, ale LangChain ma problem: {e}")
                
        else:
            print(f"Błąd pobierania: {response.status_code}")
            # kawałek błędu -> co serwer wypluł
            print(f"Treść odpowiedzi: {response.text[:200]}")

    except Exception as e:
        print(f"Błąd krytyczny: {e}")

if __name__ == "__main__":
    download_specific_unified_text()