import { useState } from "react";
import axios from "axios";
import "./App.css";

function App() {
  const [question, setQuestion] = useState("");
  const [answer, setAnswer] = useState("");
  const [loading, setLoading] = useState(false);

  const askAi = async () => {
    if (!question) return;
    setLoading(true);
    try {
      const response = await axios.post("http://localhost:8000/ask", {
        question: question,
      });
      setAnswer(response.data.answer);
    } catch (error) {
      console.error("Błąd podczas pytania:", error);
      setAnswer("Wystąpił błąd podczas łączenia z backendem.");
    }
    setLoading(false);
  };

  return (
    <div className="App">
      <h1>Asystent Prawa Pracy</h1>

      <div className="chat-window">
        <div className="input-group">
          <textarea
            value={question}
            onChange={(e) => setQuestion(e.target.value)}
            placeholder="Opisz swoją sytuację (np. pracuję 12 lat w firmie X i chcę wiedzieć ile przysługuje mi urlopu...)"
          />
          <button onClick={askAi} disabled={loading}>
            {loading ? "Analizuję przepisy..." : "Wyślij zapytanie"}
          </button>
        </div>

        {answer && (
          <div className="response-container">
            <h3>Odpowiedź Eksperta:</h3>
            <div className="response-text">{answer}</div>
          </div>
        )}
      </div>
    </div>
  );
}

export default App;
