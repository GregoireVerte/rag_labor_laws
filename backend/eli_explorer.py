import requests
import pandas as pd
import json

def explore_labor_law():
    #### Użycie endpointu ELI (European Legislation Identifier)
    #### ELI API jest ustandaryzowanym interfejsem dla systemów prawnych w UE
    #### Źródło danych: https://api.sejm.gov.pl/eli_pl.html
    base_url = "https://api.sejm.gov.pl/eli/acts"

    # pobranie listy aktów z konkretnego rocznika Kodeksu pracy (1974)
    # to pozwoli sprawdzić czy jest dostęp do danych
    url = "https://api.sejm.gov.pl/eli/acts/DU/1974"

    # headers jako przeglądarka żeby serwer nie odrzucił
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36",
        "Accept": "application/json"
    }

    print(f"--- Łączenie z API: {url} ---")
    
    try:
        response = requests.get(url, headers=headers, timeout=10)
        
        # Diagnostyka
        print(f"Status Code: {response.status_code}")
        # jeśli status nie jest 200 wypisze treść błędu
        if response.status_code != 200:
            print(f"Błąd serwera. Treść odpowiedzi: {response.text[:200]}...")
            return

        # próba odczytania JSON tylko jeśli odpowiedź nie jest pusta
        try:
            data = response.json()
        except json.JSONDecodeError:
            print("BŁĄD: Otrzymano odpowiedź, ale to nie jest JSON.")
            print(f"Surowa treść (pierwsze 500 znaków): {response.text[:500]}")
            return
        
        ### API zwraca obiekt {"items":List, "count":Int}, więc trzeba wyciągnąć listę z klucza 'items'
        acts_list = data.get('items', [])

        print(f"Pobrano strukturę danych. Liczba elementów w 'items': {len(acts_list)}")
        print("Szukam 'Kodeks pracy' w wynikach...")
        # jeśli JSON jest poprawny szuka Kodeksu pracy
        
        found = False
        target_eli = 'DU/1974/141'  # twardy identyfikator Kodeksu pracy

        print(f"Szukam konkretnego aktu o ID: {target_eli} ...")

        for act in acts_list:
            # sprawdzamy czy ELI w akcie jest identyczne z tym, którego szukamy
            if act.get('ELI') == target_eli:
                print("\nZnaleziono właściwy Kodeks pracy:")
                print(f"Tytuł: {act.get('title')}")
                print(f"ELI ID: {act.get('ELI')}")

                ### konstrukcja linków
                ### API mówi tylko "True" (że plik istnieje), trzeba zbudować link.
                ### wg dokumentacji ELI API linki do treści binarnych wyglądają tak:

                # 1. Oryginalny PDF (skan z 1974 roku)
                pdf_url = f"https://api.sejm.gov.pl/eli/acts/{target_eli}/text/pdf"

                # 2. Tekst Ujednolicony (tego szuka RAG)
                # Tekst ujednolicony zawiera wszystkie zmiany naniesione przez lata
                unified_url = f"https://api.sejm.gov.pl/eli/acts/{target_eli}/text/unified"

                print(f"\nSkonstruowane linki do pobrania:")
                print(f"-> PDF (Oryginał): {pdf_url}")
                print(f"-> PDF (Ujednolicony): {unified_url}")

                ## sprawdzenie jeszcze raz changeDate dla pewności
                details_url = f"https://api.sejm.gov.pl/eli/acts/{target_eli}"
                det_response = requests.get(details_url, headers=headers)
                if det_response.status_code == 200:
                    det_data = det_response.json()
                    print(f"-> Data ostatniej zmiany (changeDate): {det_data.get('changeDate')}")

                found = True
                break
        
        if not found:
            print(f"Nie znaleziono aktu o ID {target_eli} na liście z 1974 roku.")

    except Exception as e:
        print(f"Wystąpił błąd krytyczny: {e}")

if __name__ == "__main__":
    explore_labor_law()