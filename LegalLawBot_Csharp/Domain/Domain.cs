using System.Text.RegularExpressions;

namespace LegalLawBot_Csharp.Domain
{
    //1. Zamiast zwykłego stringa dla Art_id (np. "Art. 100")
    // Record zapewnia niemutowalność i porównywanie po wartościach.
    public record ArticleId
    {
        public string Value { get; }

        private ArticleId(string value) => Value = value;

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
}