import os
from sqlalchemy import create_engine
from sqlalchemy.ext.declarative import declarative_base
from sqlalchemy.orm import sessionmaker
from dotenv import load_dotenv

load_dotenv()

# budowanie URL połączenia: postgresql://użytkownik:hasło@host:port/nazwa_bazy ### dane zgodne z docker-compose.yml
DB_USER = os.getenv("POSTGRES_USER", "user")
DB_PASSWORD = os.getenv("POSTGRES_PASSWORD", "password")
DB_NAME = os.getenv("POSTGRES_DB", "labor_law_db")
DB_HOST = "localhost" # ponieważ main.py działa lokalnie, a baza w Dockerze na mapowanym porcie

DATABASE_URL = f"postgresql://{DB_USER}:{DB_PASSWORD}@{DB_HOST}:5432/{DB_NAME}"

# tworzenie silnika bazy danych
engine = create_engine(DATABASE_URL)

# fabryka sesji - stąd są połączenia do konkretnych zapisów
SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)

# klasa bazowa po której będą dziedziczyć modele (tabele)
Base = declarative_base()

# funkcja pomocnicza do pobierania sesji w FastAPI
def get_db():
    db = SessionLocal()
    try:
        yield db
    finally:
        db.close()