import { useState, useEffect, useRef } from "react";
import axios from "axios";
import "./App.css";

const getOrCreateSessionId = () => {
  let sessionId = localStorage.getItem("chat_session_id");
  if (!sessionId) {
    sessionId = "session-" + Math.random().toString(36).substring(2, 9);
    localStorage.setItem("chat_session_id", sessionId);
  }
  return sessionId;
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
      const response = await axios.get("http://localhost:8000/sessions");
      setSessions(response.data);
    } catch (error) {
      console.error("Błąd pobierania sesji:", error);
    }
  };

  // 2. Ładowanie historii konkretnej sesji
  const loadHistory = async (id) => {
    try {
      const response = await axios.get(`http://localhost:8000/history/${id}`);
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

  const askAi = async () => {
    if (!question.trim()) return;
    const userQuery = question;
    setQuestion("");

    const userMsg = { role: "user", text: userQuery };
    setMessages((prev) => [...prev, userMsg]);

    setLoading(true);
    try {
      const response = await axios.post("http://localhost:8000/ask", {
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
    const newId = "session-" + Math.random().toString(36).substring(2, 9);
    setSessionId(newId);
    localStorage.setItem("chat_session_id", newId);
    setMessages([]);
    setQuestion("");
    // Nie przeładowuje całej strony (window.location.reload()),
    // dzięki temu przejście jest płynne
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
                <div className="message-content">{msg.text}</div>
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
