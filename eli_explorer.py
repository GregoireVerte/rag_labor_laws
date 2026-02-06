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
        for act in acts_list:  # iteracja po liście wyciągniętej z 'items'
            # szuka po tytule (ignorując wielkość liter)
            if 'kodeks pracy' in act.get('title', '').lower():
                print("\nZnaleziono Kodeks pracy:")
                print(f"Tytuł: {act.get('title')}")
                print(f"ELI ID: {act.get('ELI')}") # kluczowe ID
                print(f"Data ogłoszenia: {act.get('promulgation')}")
                print(f"Status: {act.get('status')}")
                found = True
                
                # pobiera szczegóły tego konkretnego aktu
                eli_id = act.get('ELI')
                ### usuwanie ewentualnych spacji i kodowanie URL na wypadek dziwnych znaków, choć tu raczej ich nie ma
                details_url = f"https://api.sejm.gov.pl/eli/acts/{eli_id}"
                print(f"\nPobieranie szczegółów z: {details_url}")
                
                det_response = requests.get(details_url, headers=headers)
                if det_response.status_code == 200:
                    det_data = det_response.json()
                    print(f"Link do tekstu ujednoliconego: {det_data.get('textUnified')}")
                    print(f"Link do PDF (oryginał): {det_data.get('textPDF')}") ### czasem klucz to textPDF
                    print(f"Ostatnia zmiana (changeDate): {det_data.get('changeDate')}")
                else:
                    print("Nie udało się pobrać szczegółów.")
                break
        
        if not found:
            print("Pobrano rocznik 1974, ale nie znaleziono Kodeksu pracy na liście.")
            # wypisze pierwsze 3 akty dla sprawdzenia co przyszło
            if len(acts_list) > 0:
                print("Przykładowe 3 tytuły z listy:")
                for a in acts_list[:3]:
                    print(f"- {a.get('title')}")

    except Exception as e:
        print(f"Wystąpił błąd krytyczny: {e}")

if __name__ == "__main__":
    explore_labor_law()