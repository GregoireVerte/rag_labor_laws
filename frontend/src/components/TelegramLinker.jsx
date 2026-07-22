import React from "react";

function TelegramLinker({
  userId,
  usernameBot = "rag_labor_laws_bot",
  disabled = false,
}) {
  const telegramUrl = `https://t.me/${usernameBot}?start=${userId}`;

  return (
    <div
      className="telegram-connect-box"
      style={{
        padding: "20px",
        border: "1px solid #ccc",
        borderRadius: "8px",
        opacity: disabled ? 0.5 : 1, // Poszarzenie gdy AI myśli
        transition: "opacity 0.2s",
      }}
    >
      <h3>🤖 Połącz konto asystenta z Telegramem</h3>
      <p style={{ fontSize: "14px", color: "#666" }}>
        Chcesz zadawać pytania bezpośrednio z aplikacji Telegram? Kliknij
        poniższy przycisk, aby bezpiecznie powiązać swoje konto z Telegramem i
        zadawać pytania bezpośrednio z komunikatora. Po kliknięciu linku strona
        przekieruje Cię na oficjalną domenę Telegrama, gdzie po zalogowaniu na
        samym dole czatu zobaczysz duży przycisk START (lub Rozpocznij), który
        zakończy proces łączenia konta z botem Telegrama.
      </p>

      <a
        href={telegramUrl}
        target="_blank"
        rel="noopener noreferrer"
        onClick={(e) => disabled && e.preventDefault()} // Zapobiega otwarciu linku podczas loading (myślenia AI)
        className="btn-telegram"
        style={{
          display: "inline-block",
          backgroundColor: disabled ? "#555" : "#0088cc", // Zmiana koloru na szary podczas loading (myślenia AI)
          color: "#fff",
          padding: "10px 20px",
          borderRadius: "5px",
          textDecoration: "none",
          fontWeight: "bold",
          cursor: disabled ? "not-allowed" : "pointer", // Kursor zakazu
          pointerEvents: disabled ? "none" : "auto",
        }}
      >
        💬 Połącz z Telegramem
      </a>
    </div>
  );
}

export default TelegramLinker;
