import { useState, useEffect, useRef } from "react";
import axios from "axios";
import "./App.css";

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

  // 1. Pobieranie listy wszystkich sesji z bazy
  const fetchSessions = async () => {
    try {
      const response = await axios.get(`${API_BASE_URL}/sessions`);
      setSessions(response.data);
    } catch (error) {
      console.error("Błąd pobierania sesji:", error);
    }
  };

  // 2. Ładowanie historii konkretnej sesji
  const loadHistory = async (id) => {
    try {
      const response = await axios.get(`${API_BASE_URL}/history/${id}`);
      setMessages(response.data);
      setSessionId(id);
      localStorage.setItem("chat_session_id", id);
    } catch (error) {
      console.error("Błąd ładowania historii:", error);
    }
  };

  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  };

  // Start przy uruchomieniu: pobiera sesje i historię aktualnej sesji
  useEffect(() => {
    fetchSessions();
    if (sessionId) {
      loadHistory(sessionId);
    }
  }, []); // tylko przy montowaniu

  useEffect(() => {
    scrollToBottom();
  }, [messages, loading]);

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
      const response = await axios.post(`${API_BASE_URL}/ask`, {
        question: userQuery,
        session_id: sessionId,
      });

      const aiMsg = {
        role: "assistant",
        text: response.data.answer,
        sources: response.data.sources,
      };
      setMessages((prev) => [...prev, aiMsg]);

      // Po pierwszym pytaniu w nowej sesji odświeża listę w Sidebarze
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
      </aside>

      {/* GŁÓWNE OKNO CZATU */}
      <main className="chat-main">
        <header>
          <h1>⚖️ Asystent Prawa Pracy</h1>
        </header>

        <div className="chat-window">
          <div className="messages-container">
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

                {msg.role === "assistant" && msg.sources?.length > 0 && (
                  <div className="message-sources">
                    {msg.sources.map((src) => (
                      <span key={src} className="source-tag">
                        {src}
                      </span>
                    ))}
                  </div>
                )}
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
                (e.preventDefault(), askAi())
              }
              placeholder="Zadaj pytanie..."
            />
            <button className="send-btn" onClick={askAi} disabled={loading}>
              Wyślij
            </button>
          </div>
        </div>
      </main>
    </div>
  );
}

export default App;
