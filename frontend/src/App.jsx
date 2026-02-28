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
  const [messages, setMessages] = useState([]); // Tablica na historię dymków
  const [loading, setLoading] = useState(false);
  const [sessionId] = useState(getOrCreateSessionId()); // Stałe ID dla tej przeglądarki
  const messagesEndRef = useRef(null);

  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  };

  const askAi = async () => {
    if (!question.trim()) return;

    const userQuery = question;
    setQuestion(""); // Czyści pole od razu dla lepszego UX

    // 1. Dodaje pytanie użytkownika do widoku
    const userMsg = { role: "user", text: userQuery };
    setMessages((prev) => [...prev, userMsg]);

    setLoading(true);
    try {
      const response = await axios.post("http://localhost:8000/ask", {
        question: userQuery,
        session_id: sessionId, // Wysyła trwałe ID
      });

      // 2. Dodaje odpowiedź AI do widoku
      const aiMsg = {
        role: "assistant",
        text: response.data.answer,
        sources: response.data.sources,
      };
      setMessages((prev) => [...prev, aiMsg]);
    } catch (error) {
      console.error("Błąd:", error);
      setMessages((prev) => [
        ...prev,
        { role: "assistant", text: "Błąd połączenia z serwerem." },
      ]);
    }
    setLoading(false);
  };

  useEffect(() => {
    scrollToBottom();
  }, [messages, loading]); // 'loading', żeby zjeżdżał, gdy pojawia się "Analizuję..."

  return (
    <div className="App">
      <h1>⚖️ Asystent Prawa Pracy</h1>

      <div className="chat-window">
        {/* Kontener na dymki */}
        <div className="messages-container">
          {messages.map((msg, index) => (
            <div key={index} className={`message-bubble ${msg.role}`}>
              <div className="message-content">{msg.text}</div>

              {/* Wyświetla źródła tylko dla AI i jeśli istnieją */}
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
          {/* Na samym końcu punkt docelowy scrolla */}
          <div ref={messagesEndRef} />
        </div>

        {/* Panel wpisywania na dole */}
        <div className="input-group">
          <textarea
            value={question}
            onChange={(e) => setQuestion(e.target.value)}
            onKeyDown={(e) =>
              e.key === "Enter" && !e.shiftKey && (e.preventDefault(), askAi())
            }
            placeholder="Zadaj pytanie lub opisz swoją sytuację..."
          />
          <button onClick={askAi} disabled={loading}>
            Wyślij
          </button>
        </div>
      </div>
    </div>
  );
}

export default App;
