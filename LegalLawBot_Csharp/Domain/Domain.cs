using System.Text.RegularExpressions;

namespace LegalLawBot_Csharp.Domain
{
    //1. Zamiast zwykłego stringa dla Art_id (np. "Art. 100")
    // Record zapewnia niemutowalność i porównywanie po wartościach.
    public record ArticleId
    {
        public string Value { get; init; } // init pozwala EF Core ustawić wartość

        // Konstruktor musi być publiczny i nazwa parametru musi odpowiadać właściwości (Value -> value)
        public ArticleId(string value)
        {
            Value = value;
        }

        public static ArticleId Create(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Identyfikator artykułu nie może być pusty.");

            string trimmedValue = value.Trim();

            // 1. Obsługa wyjątku dla początku ustawy (Wstęp)
            if (trimmedValue.Equals("Wstęp", StringComparison.OrdinalIgnoreCase))
                return new ArticleId("Wstęp");

            // 2. Walidacja standardowego formatu "Art. X"
            // Regex: ^Art\. oznacza początek od "Art.", \s+ to spacja, \d+ to cyfry, [a-z] to opcjonalna mała litera
            if (!Regex.IsMatch(trimmedValue, @"^Art\.\s+\d+[a-z]*$", RegexOptions.IgnoreCase))
                throw new ArgumentException("Niepoprawny format artykułu. Oczekiwano np. 'Art. 100', 'Art. 100a' lub 'Wstęp'.");

            return new ArticleId(trimmedValue);
        }
    }

    // 2. Zamiast zwykłego stringa dla treści zapytania
    public record UserQuery
    {
        public string Text { get; }

        private UserQuery(string text) => Text = text;

        public static UserQuery Create(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < 3)
                throw new ArgumentException("Zapytanie jest zbyt krótkie.");

            // BLOKOWANIE ŚMIECI: Sprawdza, czy tekst zawiera przynajmniej jedną literę lub cyfrę.
            // Jeśli użytkownik wpisze same "$$$$$" lub "!!!!!", wyrzuci błąd.
            if (!Regex.IsMatch(text, @"[a-zA-Z0-9ąęćłńóśźżĄĘĆŁŃÓŚŹŻ]"))
                throw new ArgumentException("Zapytanie musi zawierać treść, a nie tylko znaki specjalne.");

            return new UserQuery(text.Trim());
        }
    }
    // Rekord: EmailAddress // Zamiast stringa – pełna walidacja Regexem + niemutowalność + „invalid state unrepresentable”
    public record EmailAddress
    {
        public string Value { get; }

        private EmailAddress(string value) => Value = value;

        public static EmailAddress Create(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Adres e-mail nie może być pusty.");

            string trimmedValue = value.Trim();

            // Walidacja e-mail (regex zgodny z powszechnymi RFC)
            if (!Regex.IsMatch(trimmedValue, @"^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$", RegexOptions.IgnoreCase))
                throw new ArgumentException("Niepoprawny format adresu e-mail. Oczekiwano np. 'jan.kowalski@example.com'.");

            return new EmailAddress(trimmedValue);
        }
    }
    // Rekord: UserId – opakowanie Guid (type-safe, niemutowalny)
    public record UserId
    {
        public Guid Value { get; }

        private UserId(Guid value) => Value = value;

        public static UserId Create(Guid value)
        {
            if (value == Guid.Empty)
                throw new ArgumentException("Identyfikator użytkownika nie może być pusty (Guid.Empty).");

            return new UserId(value);
        }

        // Fabryka do tworzenia nowego użytkownika
        public static UserId New() => Create(Guid.NewGuid());
    }
    // Rekord: TelegramChatId – opakowanie dla 64-bitowego ID z Telegrama (type-safe)
    public record TelegramChatId
    {
        public long Value { get; }

        private TelegramChatId(long value) => Value = value;

        public static TelegramChatId Create(long value)
        {
            if (value == 0)
                throw new ArgumentException("Identyfikator Telegram Chat ID nie może być zerem.");

            return new TelegramChatId(value);
        }
    }
    // Wspierający rekord: UserStatus – status jako value object (unikanie stringa i enuma)
    public record UserStatus
    {
        public string Value { get; }

        private UserStatus(string value) => Value = value;

        public static UserStatus Aktywny { get; } = new UserStatus("Aktywny");
        public static UserStatus Zablokowany { get; } = new UserStatus("Zablokowany"); // Przygotowanie pod przyszłą logikę
    }
    // Record: UserRole – zapobiega wpisywaniu dowolnych stringów (Type Safety)
    public record UserRole
    {
        public string Name { get; }
        private UserRole(string name) => Name = name;

        // Definicja tylko dozwolonych ról
        public static UserRole Standard { get; } = new UserRole("Standard");
        public static UserRole Administrator { get; } = new UserRole("Administrator");
    }
    // Klasa domenowa: User (entity)
    // – zaczyna zawsze jako Aktywny
    // – używa wyłącznie bogatych typów domenowych
    // – prywatny konstruktor + fabryka = invalid states unrepresentable
    public class User
    {
        public UserId Id { get; private set; }
        public EmailAddress Email { get; private set; }
        public UserStatus Status { get; private set; }
        public UserRole Role { get; private set; }
        public TelegramChatId? TelegramChatId { get; private set; } // opcjonalne mapowanie (Telegram)

        // Trwała pamięć aktywnej rozmowy Telegrama w bazie danych (nullable, bo rozmowa może być zamknięta)
        public Guid? ActiveConsultationId { get; private set; }

        public int DailyQueryCount { get; private set; }
        public int MaxDailyLimit { get; private set; }

        // Pusty konstruktor - wymagany przez EF Core do odtwarzania użytkownika z bazy
        private User()
        {
            Id = null!;
            Email = null!;
            Status = null!;
            Role = null!;
            ActiveConsultationId = null;
        }

        // Prywatny konstruktor – tylko fabryka może tworzyć obiekt
        private User(UserId id, EmailAddress email)
        {
            Id = id;
            Email = email;
            Status = UserStatus.Aktywny; // zawsze startuje jako Aktywny
            Role = UserRole.Standard; // domyślnie każdy jest zwykłym użytkownikiem
            DailyQueryCount = 0;   // start z zerem zapytań
            MaxDailyLimit = 10;    // domyślny limit zapytań na dzień
        }

        // Jedyny publiczny sposób tworzenia poprawnego użytkownika
        public static User Create(UserId id, EmailAddress email)
        {
            // dodatkowe walidacje domenowe można dodać tutaj (np. biznesowe reguły)
            // Nawet jeśli typy są bezpieczne, sprawdza czy same obiekty nie są nullem
            var userId = id ?? throw new ArgumentNullException(nameof(id));
            var userEmail = email ?? throw new ArgumentNullException(nameof(email));

            return new User(userId, userEmail);
        }

        // Metoda dostępna tylko dla logiki biznesowej (np. nadanie uprawnień przez system)
        public void PromoteToAdmin()
        {
            Role = UserRole.Administrator;
        }

        // Przykład metody domenowej (można rozbudować później)
        public void ChangeEmail(EmailAddress newEmail)
        {
            Email = newEmail;
        }
        // metoda biznesowa do bezpiecznego powiązania konta z Telegramem
        public void LinkTelegram(TelegramChatId telegramChatId)
        {
            TelegramChatId = telegramChatId ?? throw new ArgumentNullException(nameof(telegramChatId));
        }
        // Ustawia nową lub kolejną aktywną sesję rozmowy
        public void SetActiveConsultation(Guid consultationId)
        {
            if (consultationId == Guid.Empty)
                throw new ArgumentException("Identyfikator konsultacji nie może być pusty (Guid.Empty).", nameof(consultationId));

            ActiveConsultationId = consultationId;
        }

        // Czyści pamięć podręczną (wywoływane przez komendę /reset)
        public void ClearActiveConsultation()
        {
            ActiveConsultationId = null;
        }

        // Metoda biznesowa: Zwiększa licznik lub rzuca błąd, jeśli przekroczono max. limit
        public void IncrementQueryCount()
        {
            if (DailyQueryCount >= MaxDailyLimit)
                throw new InvalidOperationException("Osiągnięto dzienny limit zapytań AI. Wymagane przejście na plan Premium.");

            DailyQueryCount++;
        }

        // Metoda biznesowa: Bezpieczne zerowanie licznika (wywoływane przez proces w tle)
        public void ResetDailyQueries()
        {
            DailyQueryCount = 0;
        }

        // Metoda pomocnicza (np. dla przyszłego panelu admina do zwiększania limitów)
        public void UpdateMaxDailyLimit(int newLimit)
        {
            if (newLimit < 0)
                throw new ArgumentException("Limit zapytań nie może być liczbą ujemną.", nameof(newLimit));

            MaxDailyLimit = newLimit;
        }
    }
}