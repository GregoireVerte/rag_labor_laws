import requests
import os
from langchain_community.document_loaders import PyPDFLoader
from datetime import datetime
import json


def get_latest_labor_code_automated():
    """Wyszukuje ostatni jednolity tekst ustawy w zakresie pięciu ostatnich lat, zapisuje i zwraca jego url oraz identyfikator ELI."""

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
                    
                    ###address = f"W{publisher}{year_act}{pos}" # Buduje address: W + DU + 2025 + 0000277
                    
                    # Pobiera szczegóły żeby wyciągnąć dokładną nazwę pliku
                    eli_id = act.get('ELI')
                    details_url = f"https://api.sejm.gov.pl/eli/acts/{eli_id}"
                    det_res = requests.get(details_url, headers=headers)
                    
                    if det_res.status_code == 200:
                        det_data = det_res.json()
                        change_date = det_data.get('changeDate')
                        nowelizacje = det_data.get('references', {}).get('Nowelizacje po tekście jednolitym', [])

                        print(f"Ostatnia aktualizacja w ISAP: {change_date}")
                        if nowelizacje:
                            print(f"Liczba nowelizacji po tekście jednolitym: {len(nowelizacje)}")
                            print(f"Najnowsza nowelizacja: {nowelizacje[-1].get('id')}")

                        if not should_update(eli_id, change_date):
                            print("Posiadasz już najnowszą dostępną wersję tekstu.")
                            return None, None, None

                        address = det_data.get('address') # wyciąga address z detali
                        if not address:
                            print(f"Pomijanie {eli_id}: Brak pola 'address' w szczegółach aktu.")
                            continue
                        file_name = None
                        # szuka pliku typu "U" (Ujednolicony)
                        for text_obj in det_data.get('texts', []):
                            if text_obj.get('type') == 'U':
                                file_name = text_obj.get('fileName')
                                break
                        
                        if file_name:
                            final_url = f"https://isap.sejm.gov.pl/isap.nsf/download.xsp/{address}/U/{file_name}"
                            print(f"\nAUTOMATYCZNY LINK: {final_url}")
                            return final_url, eli_id, change_date

        except requests.exceptions.Timeout:
            print(f"Błąd: Przekroczono czas oczekiwania dla rocznika {year}.")
        except requests.exceptions.RequestException as e:
            print(f"Błąd sieciowy: {e}")
        except Exception as e:
            print(f"Nieoczekiwany błąd: {e} dla rocznika {year}")

    return None, None, None


def download_specific_unified_text(target_eli, pdf_url):
    """Pobiera wybrany tekst jednolity ustawy, zapisuje go lokalnie i sprawdza czy LangChain widzi tekst poprawnie.

    Args:
        target_eli (str): Konkretny identyfikator ID ELI ostatniego dużego tekstu jednolitego.
        pdf_url (str): Bezpośredni link do oficjalnego PDF z Dziennika Ustaw
            (skan/cyfrowy oryginał, nie wygenerowany dynamicznie).
    """

    file_path = "last_unified_labor_code.pdf"

    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36"
    }

    print(f"Tekst jednolity Kodeksu pracy ({target_eli})")
    print(f"Pobieranie z: {pdf_url}...")
    
    try:
        response = requests.get(pdf_url, headers=headers, timeout=30)
        
        # Diagnostyka: jeśli to nie jest PDF (200)
        if response.status_code == 200:
            # sprawdzenie czy to na pewno PDF, a nie np. ukryta strona błędu HTML
            content_type = response.headers.get('Content-Type', '').lower()
            if 'application/pdf' not in content_type:
                print(f"Błąd: Otrzymano {content_type} zamiast application/pdf. Pobieranie przerwane.")
                return False ### Zwraca False, bo to nie PDF
            
            ## zapis pliku
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
            return True
                
        else:
            print(f"Błąd pobierania: {response.status_code}")
            # kawałek błędu -> co serwer wypluł
            print(f"Treść odpowiedzi: {response.text[:200]}")
            return False

    except requests.exceptions.Timeout:
        print(f"Błąd: Przekroczono czas oczekiwania.")
        return False
    except requests.exceptions.RequestException as e:
        print(f"Błąd sieciowy: {e}")
        return False
    except Exception as e:
        print(f"Nieoczekiwany błąd: {e}")
        return False


def should_update(new_eli, new_change_date):
    """Sprawdza czy jest już ta wersja pliku lokalnie."""
    meta_file = "pdf_metadata.json"
    if not os.path.exists(meta_file):
        return True
    
    with open(meta_file, 'r') as f:
        old_meta = json.load(f)
    
    ### jeśli ELI jest inne lub data zmiany jest nowsza - aktualizuje
    if old_meta.get("eli") != new_eli or old_meta.get("changeDate") < new_change_date:
        return True
    return False

def save_metadata(eli, change_date):
    with open("pdf_metadata.json", 'w') as f:
        json.dump({"eli": eli, "changeDate": change_date}, f)



if __name__ == "__main__":
    ## 1. Szuka najnowszej wersji i pobiera change_date
    url, eli, c_date = get_latest_labor_code_automated()

    if not url:
        print("\nNie udało się automatycznie wygenerować linku lub posiadasz aktualną wersję.")
    else:
        ## 2. Próbuje pobrać plik
        success = download_specific_unified_text(target_eli=eli, pdf_url=url)
        
        ## 3. Jeśli pobieranie się udało, zapisuje metadane
        if success:
            save_metadata(eli, c_date)
            print("Metadane zostały zaktualizowane.")