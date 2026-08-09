import React from "react";

function TelegramLinker({
  userId,
  usernameBot = "rag_labor_laws_bot",
  disabled = false,
  isLinked = false,
}) {
  const telegramUrl = `https://t.me/${usernameBot}?start=${userId}`;
  const isBtnDisabled = disabled || isLinked;

  return (
    <div
      className="telegram-connect-box"
      style={{
        padding: "20px",
        backgroundColor: "#222",
        border: "1px solid #333",
        borderRadius: "8px",
        opacity: disabled ? 0.5 : 1, // Poszarzenie gdy AI myśli
        transition: "opacity 0.2s",
      }}
    >
      <h3>🤖 Połącz konto asystenta z Telegramem</h3>
      <p style={{ fontSize: "14px", color: "#666" }}>
        {isLinked
          ? "Twoje konto jest już pomyślnie powiązane z botem Telegrama. Możesz w każdej chwili pisać do asystenta bezpośrednio w aplikacji Telegram na swoim telefonie lub komputerze."
          : "Chcesz zadawać pytania bezpośrednio z aplikacji Telegram? Kliknij poniższy przycisk, aby bezpiecznie powiązać swoje konto z Telegramem i zadawać pytania bezpośrednio z komunikatora, np. na swoim telefonie. Po kliknięciu linku strona przekieruje Cię na oficjalną domenę Telegrama, gdzie po zalogowaniu na samym dole czatu zobaczysz duży przycisk ROZPOCZNIJ (lub START), który zakończy proces łączenia konta asystenta z botem Telegrama. Możesz to zrobić na telefonie z Telegramem lub na komputerze z aplikacją Telegram Desktop (pamiętaj, że w szczególności na komputerze / laptopie musisz być zalogowany na swoim właściwym koncie Telegrama, żeby nie zsynchronizować się przypadkowo z niewłaściwym kontem Telegrama, jeżeli współdzielisz urządzenie z innymi domownikami)."}
      </p>

      {/* Kontener span zapewnia poprawne wyświetlanie kursora not-allowed oraz tooltipu (title) */}
      <span
        title={
          isLinked
            ? "Konto jest już połączone z Telegramem"
            : disabled
              ? "Przetwarzanie zlecenia..."
              : "Kliknij, aby połączyć z Telegramem"
        }
        style={{
          display: "inline-block",
          cursor: isBtnDisabled ? "not-allowed" : "pointer",
        }}
      >
        <a
          href={telegramUrl}
          target="_blank"
          rel="noopener noreferrer"
          onClick={(e) => isBtnDisabled && e.preventDefault()} // Zapobiega otwarciu linku przy zablokowaniu m.in. podczas loading (myślenia AI)
          className="btn-telegram"
          style={{
            display: "inline-block",
            backgroundColor: isLinked ? "#444" : disabled ? "#555" : "#0088cc", // Wyrazisty szary kolor po połączeniu
            color: isLinked ? "#aaa" : "#fff",
            padding: "10px 20px",
            borderRadius: "5px",
            textDecoration: "none",
            fontWeight: "bold",
            cursor: isBtnDisabled ? "not-allowed" : "pointer", // Kursor zakazu
            opacity: isBtnDisabled ? 0.7 : 1,
            border: isLinked ? "1px solid #555" : "none",
          }}
        >
          {isLinked
            ? "✅ Konto połączone z Telegramem"
            : "💬 Połącz z Telegramem"}
        </a>
      </span>
    </div>
  );
}

export default TelegramLinker;
