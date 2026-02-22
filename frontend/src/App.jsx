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
      <div className="chat-container">
        <textarea
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          placeholder="Zadaj pytanie dotyczące Kodeksu Pracy..."
        />
        <button onClick={askAi} disabled={loading}>
          {loading ? "Myślę..." : "Zapytaj"}
        </button>
      </div>
      {answer && (
        <div className="answer-box">
          <h3>Odpowiedź:</h3>
          <p>{answer}</p>
        </div>
      )}
    </div>
  );
}

export default App;
