import { useState, useEffect, useRef } from "react";
import axios from "axios";
import "./App.css";
import { supabase } from "./supabaseClient";

const API_BASE_URL = import.meta.env.VITE_API_URL;

const getOrCreateSessionId = () => {
  // Jeśli w pamięci przeglądarki jest już zapisany stary Guid, pobierz go
  let sessionId = localStorage.getItem("chat_session_id");
  // Jeśli zawierał stary prefix "session-", wyczyść go
  if (sessionId && sessionId.includes("session-")) {
    localStorage.removeItem("chat_session_id");
    return null;
  }
  return sessionId || null; // Zwraca poprawny Guid lub null
};

function App() {
  const [question, setQuestion] = useState("");
  const [messages, setMessages] = useState([]);
  const [sessions, setSessions] = useState([]); // Lista sesji do Sidebaru
  const [loading, setLoading] = useState(false);
  const [sessionId, setSessionId] = useState(getOrCreateSessionId());
  const messagesEndRef = useRef(null);
  // Stan użytkownika - domyślnie pusty (null), nikt nie jest zalogowany
  const [user, setUser] = useState(null);
  // Stan sterujący tym, czy pokazuje formularz logowania ('login'), czy rejestracji ('register')
  const [authMode, setAuthMode] = useState("login");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [authError, setAuthError] = useState(""); // tu zapisze ewentualne komunikaty o błędach (np. złe hasło)

  // Funkcja obsługująca rejestrację nowego konta
  const handleRegister = async (e) => {
    e.preventDefault(); // Zapobiega przeładowaniu strony po wysłaniu formularza
    setAuthError("");
    setLoading(true);

    // Wysyła dane do Supabase Auth
    const { data, error } = await supabase.auth.signUp({
      email: email,
      password: password,
    });

    if (error) {
      // Jeśli Supabase zwróci błąd (np. słabe hasło), zapisuje go w stanie
      setAuthError(error.message);
    } else if (data?.user) {
      // Jeśli wszystko poszło ok zapisuje użytkownika w stanie aplikacji
      setUser(data.user);
      setEmail("");
      setPassword("");
    }
    setLoading(false);
  };

  // Funkcja obsługująca logowanie na istniejące konto
  const handleLogin = async (e) => {
    e.preventDefault();
    setAuthError("");
    setLoading(true);

    // Prosi Supabase o zalogowanie za pomocą maila i hasła
    const { data, error } = await supabase.auth.signInWithPassword({
      email: email,
      password: password,
    });

    if (error) {
      setAuthError(error.message); // np. "Invalid login credentials"
    } else if (data?.user) {
      setUser(data.user);
      setEmail("");
      setPassword("");
    }
    setLoading(false);
  };

  // Funkcja obsługująca wylogowanie użytkownika
  const handleLogout = async () => {
    const { error } = await supabase.auth.signOut();

    if (error) {
      console.error("Błąd podczas wylogowywania:", error.message);
    } else {
      // Czyszczenie całego stanu aplikacji do zera
      setUser(null);
      setMessages([]);
      setSessions([]);
      setSessionId(null);
      localStorage.removeItem("chat_session_id");
    }
  };

  // 1. Pobieranie listy wszystkich sesji z bazy (Wersja Dynamiczna)
  const fetchSessions = async () => {
    if (!user) return; // Zabezpieczenie: nie pobieraj jeśli nikt nie jest zalogowany
    try {
      const response = await axios.get(
        `${API_BASE_URL}/sessions?userId=${user.id}`,
      );
      setSessions(response.data);
    } catch (error) {
      console.error("Błąd pobierania sesji:", error);
    }
  };

  // 2. Ładowanie historii konkretnej sesji
  const loadHistory = async (id) => {
    try {
      const response = await axios.get(`${API_BASE_URL}/history/${id}`);

      // Sprawdza czy .NET przysłał obiekt z polem 'history', jeśli nie, bierze czysty response
      const historyArray = response.data.history || response.data;

      // ustawia tablicę żeby .map() nigdy się nie wywalił
      setMessages(Array.isArray(historyArray) ? historyArray : []);

      setSessionId(id);
      localStorage.setItem("chat_session_id", id);
    } catch (error) {
      console.error("Błąd ładowania historii:", error);
    }
  };

  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  };

  // Start przy uruchomieniu lub zmianie użytkownika: pobiera sesje i historię
  useEffect(() => {
    if (user) {
      fetchSessions();
      if (sessionId) {
        loadHistory(sessionId);
      }
    }
  }, [user]); // Zmiana tablicy zależności z [] na [user]

  useEffect(() => {
    scrollToBottom();
  }, [messages, loading]);

  // Mechanizm automatycznego wylogowania po 15 minutach bezczynności
  useEffect(() => {
    // Jeśli nikt nie jest zalogowany, nie uruchamia stopera
    if (!user) return;

    let timeoutId;
    const INACTIVITY_TIME = 15 * 60 * 1000; // 15 minut (w milisekundach)

    const resetTimer = () => {
      // Jeśli stoper już odliczał, kasuje go i zaczyna odliczanie od nowa
      if (timeoutId) clearTimeout(timeoutId);

      timeoutId = setTimeout(() => {
        alert("Zostałeś automatycznie wylogowany z powodu bezczynności.");
        handleLogout();
      }, INACTIVITY_TIME);
    };

    // Lista zdarzeń, które uznaje za "aktywność" użytkownika
    const activityEvents = [
      "mousemove",
      "keydown",
      "mousedown",
      "scroll",
      "touchstart",
    ];

    // Rejestruje nasłuchiwanie na każde z tych zdarzeń w oknie przeglądarki
    activityEvents.forEach((event) => {
      window.addEventListener(event, resetTimer);
    });

    // Uruchamia stoper po raz pierwszy przy wejściu do aplikacji
    resetTimer();

    // Funkcja czyszcząca (cleanup) - usuwa nasłuchiwanie, gdy użytkownik się wyloguje
    return () => {
      if (timeoutId) clearTimeout(timeoutId);
      activityEvents.forEach((event) => {
        window.removeEventListener(event, resetTimer);
      });
    };
  }, [user]); // Ten efekt uruchomi się na nowo za każdym razem, gdy zmieni się stan 'user'

  const copyToClipboard = (text) => {
    navigator.clipboard.writeText(text);
    alert("Skopiowano do schowka!");
  };

  const askAi = async () => {
    if (!question.trim()) return;
    const userQuery = question;
    setQuestion("");

    const userMsg = { role: "user", text: userQuery };
    setMessages((prev) => [...prev, userMsg]);

    setLoading(true);
    try {
      // Wysyła zapytanie do C# dorzucając unikalne ID zalogowanego użytkownika i czeka na odpowiedź
      const response = await axios.post(`${API_BASE_URL}/ask`, {
        question: userQuery,
        session_id: sessionId,
        user_id: user.id,
      });

      // Mapuje odpowiedź od asystenta
      const aiMsg = {
        role: "assistant",
        text: response.data.answer,
        sources: response.data.sources,
      };
      setMessages((prev) => [...prev, aiMsg]);

      // jeśli to był nowy wątek zapisuje ID z C#
      if (!sessionId) {
        setSessionId(response.data.id);
        localStorage.setItem("chat_session_id", response.data.id);
      }

      // Po udanej odpowiedzi odświeża listę sesji w Sidebarze
      fetchSessions();
    } catch (error) {
      console.error("Błąd:", error);
      setMessages((prev) => [
        ...prev,
        { role: "assistant", text: "Błąd połączenia z serwerem." },
      ]);
    }
    setLoading(false);
  };

  const startNewChat = () => {
    setSessionId(null);
    localStorage.removeItem("chat_session_id");
    setMessages([]);
    setQuestion("");
  };

  const deleteSession = async (e, id) => {
    e.stopPropagation(); // Ważne: zapobiega otwarciu sesji przy kliknięciu w "X"
    if (!window.confirm("Czy na pewno chcesz usunąć tę rozmowę?")) return;

    try {
      await axios.delete(`${API_BASE_URL}/sessions/${id}`);

      // Usuwa sesję z lokalnego stanu, żeby zniknęła z listy
      setSessions((prev) => prev.filter((s) => s.id !== id));

      // Jeśli usunięto sesję, która jest obecnie otwarta - zacznie nowy wątek
      if (id === sessionId) {
        startNewChat();
      }
    } catch (error) {
      console.error("Błąd podczas usuwania sesji:", error);
    }
  };

  // Jeśli użytkownik nie jest zalogowany, przerywa i pokazuje ekran logowania/rejestracji
  if (!user) {
    return (
      <div
        className="auth-container"
        style={{
          display: "flex",
          justifyContent: "center",
          alignItems: "center",
          height: "100vh",
          backgroundColor: "#1e1e1e",
          color: "#fff",
        }}
      >
        <div
          className="auth-box"
          style={{
            background: "#2a2a2a",
            padding: "30px",
            borderRadius: "8px",
            width: "100%",
            maxWidth: "400px",
            boxShadow: "0 4px 15px rgba(0,0,0,0.5)",
          }}
        >
          <h2 style={{ textAlign: "center", marginBottom: "20px" }}>
            {authMode === "login"
              ? "⚖️ Zaloguj się do Asystenta Prawa Pracy"
              : "📝 Stwórz konto"}
          </h2>

          <form
            onSubmit={authMode === "login" ? handleLogin : handleRegister}
            style={{ display: "flex", flexDirection: "column", gap: "15px" }}
          >
            <div
              style={{ display: "flex", flexDirection: "column", gap: "5px" }}
            >
              <label style={{ fontSize: "14px", color: "#aaa" }}>E-mail:</label>
              <input
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                style={{
                  padding: "10px",
                  borderRadius: "4px",
                  border: "1px solid #444",
                  backgroundColor: "#333",
                  color: "#fff",
                }}
              />
            </div>

            <div
              style={{ display: "flex", flexDirection: "column", gap: "5px" }}
            >
              <label style={{ fontSize: "14px", color: "#aaa" }}>Hasło:</label>
              <input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                style={{
                  padding: "10px",
                  borderRadius: "4px",
                  border: "1px solid #444",
                  backgroundColor: "#333",
                  color: "#fff",
                }}
              />
            </div>

            {authError && (
              <div
                className="auth-error"
                style={{
                  color: "#ff6b6b",
                  fontSize: "14px",
                  textAlign: "center",
                }}
              >
                ⚠️ {authError}
              </div>
            )}

            <button
              type="submit"
              disabled={loading}
              style={{
                padding: "12px",
                borderRadius: "4px",
                border: "none",
                backgroundColor: "#0066cc",
                color: "#fff",
                fontWeight: "bold",
                cursor: "pointer",
                marginTop: "10px",
              }}
            >
              {loading
                ? "Przetwarzanie..."
                : authMode === "login"
                  ? "Zaloguj się"
                  : "Zarejestruj się"}
            </button>
          </form>

          <div
            style={{ textAlign: "center", marginTop: "20px", fontSize: "14px" }}
          >
            {authMode === "login" ? (
              <p>
                Nie masz konta?{" "}
                <span
                  onClick={() => {
                    setAuthMode("register");
                    setAuthError("");
                  }}
                  style={{
                    color: "#0088ff",
                    cursor: "pointer",
                    textDecoration: "underline",
                  }}
                >
                  Zarejestruj się
                </span>
              </p>
            ) : (
              <p>
                Masz już konto?{" "}
                <span
                  onClick={() => {
                    setAuthMode("login");
                    setAuthError("");
                  }}
                  style={{
                    color: "#0088ff",
                    cursor: "pointer",
                    textDecoration: "underline",
                  }}
                >
                  Zaloguj się
                </span>
              </p>
            )}
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="layout">
      {/* SIDEBAR */}
      <aside className="sidebar">
        <button onClick={startNewChat} className="new-chat-btn">
          + Nowy wątek
        </button>
        <div className="sessions-list">
          {sessions.map((s) => (
            <div
              key={s.id}
              className={`session-item ${s.id === sessionId ? "active" : ""}`}
              onClick={() => loadHistory(s.id)}
            >
              <span className="session-icon">💬</span>
              <span className="session-title">{s.title}</span>

              {/* Przycisk usuwania */}
              <button
                className="delete-session-btn"
                onClick={(e) => deleteSession(e, s.id)}
              >
                ×
              </button>
            </div>
          ))}
        </div>
        {/* PROFIL UŻYTKOWNIKA (BEZPIECZNA WERSJA) Z PRZYCISKIEM WYLOGOWANIA */}
        <div
          className="sidebar-footer"
          style={{
            marginTop: "auto",
            padding: "15px 0 0 0",
            borderTop: "1px solid #333",
          }}
        >
          <div
            className="user-profile"
            style={{ display: "flex", alignItems: "center", gap: "10px" }}
          >
            <span style={{ fontSize: "20px" }}>👤</span>
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                width: "100%",
              }}
            >
              <span
                style={{
                  fontWeight: "bold",
                  fontSize: "14px",
                  color: "#fff",
                  textOverflow: "ellipsis",
                  overflow: "hidden",
                  whiteSpace: "nowrap",
                }}
              >
                {user ? user.email : "Gość"}
              </span>
              <span
                style={{
                  fontSize: "12px",
                  color: "#aaa",
                  marginBottom: user ? "8px" : "0",
                }}
              >
                {user ? "Zalogowany" : "Niezalogowany"}
              </span>

              {/* Jeśli użytkownik jest zalogowany, pokaże przycisk wylogowania */}
              {user && (
                <button
                  onClick={handleLogout}
                  style={{
                    padding: "6px 10px",
                    borderRadius: "4px",
                    border: "1px solid #ff4d4d",
                    backgroundColor: "transparent",
                    color: "#ff4d4d",
                    fontSize: "12px",
                    fontWeight: "bold",
                    cursor: "pointer",
                    textAlign: "center",
                    transition: "all 0.2s",
                  }}
                  onMouseOver={(e) => {
                    e.target.style.backgroundColor = "#ff4d4d";
                    e.target.style.color = "#fff";
                  }}
                  onMouseOut={(e) => {
                    e.target.style.backgroundColor = "transparent";
                    e.target.style.color = "#ff4d4d";
                  }}
                >
                  🚪 Wyloguj się
                </button>
              )}
            </div>
          </div>
        </div>
      </aside>

      {/* GŁÓWNE OKNO CZATU */}
      <main className="chat-main">
        <header>
          <h1>⚖️ Asystent Prawa Pracy</h1>
        </header>

        <div className="chat-window">
          <div className="messages-container">
            {/* Ekran powitalny (Empty State) gdy nie ma jeszcze wiadomości */}
            {messages.length === 0 && !loading && (
              <div className="chat-empty-state">
                <div className="empty-icon">⚖️</div>
                <h2>W czym mogę Ci dzisiaj pomóc?</h2>
                <p>
                  Zadaj pytanie dotyczące Kodeksu Pracy, np. o urlopy, okres
                  wypowiedzenia czy umowy o pracę.
                </p>
              </div>
            )}

            {messages.map((msg, index) => (
              <div key={index} className={`message-bubble ${msg.role}`}>
                <div className="message-content">{msg.content || msg.text}</div>

                {/* Przycisk kopiowania dla każdej wiadomości chatu (User i AI) */}
                <button
                  className="copy-btn"
                  onClick={() => copyToClipboard(msg.content || msg.text)}
                  title="Kopiuj treść"
                >
                  📋
                </button>

                {msg.role === "assistant" &&
                  msg.sources &&
                  (() => {
                    // Normalizacja: jeśli to string z bazy, rozbij go po przecinkach. Jeśli tablica - zostaw.
                    const parsedSources = Array.isArray(msg.sources)
                      ? msg.sources
                      : typeof msg.sources === "string"
                        ? msg.sources
                            .split(",")
                            .map((s) => s.trim())
                            .filter(Boolean)
                        : [];

                    if (parsedSources.length === 0) return null;

                    return (
                      <div className="message-sources">
                        {parsedSources.map((src) => (
                          <span key={src} className="source-tag">
                            {src}
                          </span>
                        ))}
                      </div>
                    );
                  })()}
              </div>
            ))}
            {loading && (
              <div className="message-bubble assistant loading">
                Analizuję przepisy...
              </div>
            )}
            <div ref={messagesEndRef} />
          </div>

          <div className="input-group">
            <textarea
              value={question}
              onChange={(e) => setQuestion(e.target.value)}
              onKeyDown={(e) =>
                e.key === "Enter" &&
                !e.shiftKey &&
                question.length <= 1000 && // enter nie wyśle jeśli za długie
                (e.preventDefault(), askAi())
              }
              placeholder="Zadaj pytanie..."
            />
            <button
              className="send-btn"
              onClick={askAi}
              disabled={loading || !question.trim() || question.length > 1000}
            >
              Wyślij
            </button>
          </div>

          {/* Licznik znaków informujący użytkownika o limicie */}
          <div
            className={`char-counter ${question.length > 1000 ? "exceeded" : ""}`}
          >
            {question.length} / 1000 znaków
          </div>
        </div>
      </main>
    </div>
  );
}

export default App;
