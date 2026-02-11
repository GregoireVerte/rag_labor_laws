import requests
from datetime import datetime

def get_latest_labor_code_automated():
    ## dynamiczne generowanie listy 5 ostatnich lat (od najnowszego)
    current_year = datetime.now().year
    years_to_check = [current_year - i for i in range(5)]

    headers = {
        "User-Agent": "Mozilla/5.0",
        "Accept": "application/json"
    }

    print(f"Sprawdzanie roczników: {years_to_check}")

    for year in years_to_check:
        # użycie stabilnego punktu dla konkretnego rocznika Dziennika Ustaw (DU)
        url = f"https://api.sejm.gov.pl/eli/acts/DU/{year}"
        print(f"--- Sprawdzenie rocznika {year} pod adresem: {url} ---")
        
        try:
            response = requests.get(url, headers=headers, timeout=15)
            
            ## jeśli rocznik nie istnieje (np. początek stycznia) idzie dalej
            if response.status_code != 200:
                print(f"Rocznik {year} niedostępny lub błąd: {response.status_code}")
                continue

            data = response.json()
            # API zwraca słownik z kluczem 'items'
            items = data.get('items', [])

            ## sortowanie items po 'pos' (pozycji) malejąco aby zacząć od najnowszych aktów wewnątrz rocznika
            items.sort(key=lambda x: x.get('pos', 0), reverse=True)

            for act in items:
                title = act.get('title', '')
                # szuka Obwieszczenia o tekście jednolitym Kodeksu Pracy
                if "Kodeks pracy" in title and "jednolitego tekstu" in title.lower():
                    print(f"Znaleziono w roczniku {year}: {title}")
                    
                    # Dane do budowy linku ISAP
                    publisher = act.get('publisher')
                    year_act = act.get('year')
                    pos = str(act.get('pos')).zfill(7) # dopełnienie do 7 cyfr (np.: 0000277)
                    
                    # Buduje address: W + DU + 2025 + 0000277
                    address = f"W{publisher}{year_act}{pos}"
                    
                    # Pobiera szczegóły żeby wyciągnąć dokładną nazwę pliku
                    eli_id = act.get('ELI')
                    details_url = f"https://api.sejm.gov.pl/eli/acts/{eli_id}"
                    det_res = requests.get(details_url, headers=headers)
                    
                    if det_res.status_code == 200:
                        det_data = det_res.json()
                        file_name = None
                        # szuka pliku typu "U" (Ujednolicony) 
                        for text_obj in det_data.get('texts', []):
                            if text_obj.get('type') == 'U':
                                file_name = text_obj.get('fileName')
                                break
                        
                        if file_name:
                            final_url = f"https://isap.sejm.gov.pl/isap.nsf/download.xsp/{address}/U/{file_name}"
                            print(f"\nAUTOMATYCZNY LINK: {final_url}")
                            return final_url

        except Exception as e:
            print(f"Błąd dla rocznika {year}: {e}")

    return None

if __name__ == "__main__":
    url = get_latest_labor_code_automated()
    if not url:
        print("\nNie udało się automatycznie wygenerować linku.")