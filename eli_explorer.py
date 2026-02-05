import requests
import pandas as pd
import json

def explore_labor_law():
    #### Adres bazowy API Sejmu (ELI)
    #### ELI API jest ustandaryzowanym interfejsem dla systemów prawnych w UE
    base_url = "https://api.sejm.gov.pl/eli_pl"
    
    ##### Parametry wyszukiwania: szukanie konkretnie Kodeksu pracy
    ##### Skupienie na głównym akcie, aby zrozumieć jego strukturę
    params = {
        "title": "Kodeks pracy",
        "limit": 5,  ## żeby zobaczyć kilka ostatnich wersji lub powiązanych aktów
        "format": "json"
    }

    print(f"--- Łączenie z API: {base_url}/acts ---")
    
    try:
        response = requests.get(f"{base_url}/acts", params=params)
        response.raise_for_status() # sprawdzenie czy zapytanie się udało
        
        data = response.json()
        acts = data.get("acts", [])

        if not acts:
            print("Nie znaleziono aktów o takim tytule.")
            return

        # Przekształcenie danych do DataFrame, aby łatwiej je analizować
        df = pd.DataFrame(acts)
        
        # Wybów kluczowych kolumn dla RAG:
        ### ELI - unikalny identyfikator dokumentu
        ### promulgation - data ogłoszenia (ważna do aktualizacji przyrostowych)
        ### title - pełny tytuł
        ### status - czy akt jest obowiązujący
        relevant_columns = ['title', 'ELI', 'promulgation', 'status', 'type']
        display_df = df[relevant_columns]

        print("\nZidentyfikowane akty prawne:")
        print(display_df.to_string(index=False))

        # szczegóły pierwszego (najważniejszego) wyniku
        main_act_eli = acts[0]['ELI']
        print(f"\n--- Szczegóły dla głównego aktu (ELI: {main_act_eli}) ---")
        
        # Pobieranie pełnych metadanych konkretnego aktu
        details_url = f"{base_url}/acts/{main_act_eli}"
        details_response = requests.get(details_url, params={"format": "json"})
        details_data = details_response.json()

        # Wyświetlanie linku do PDF - źródło dla parsera
        pdf_url = details_data.get('textPDF_url')
        change_date = details_data.get('changeDate')
        
        print(f"Link do pliku PDF: {pdf_url}")
        print(f"Ostatnia zmiana w metadanych: {change_date}")
        
    except Exception as e:
        print(f"Wystąpił błąd podczas komunikacji z API: {e}")

if __name__ == "__main__":
    explore_labor_law()