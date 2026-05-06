namespace LegalLawBot_Csharp.Domain;

using System;
using System.Collections.Generic;

// Baza dla wszystkich stanów konsultacji
public abstract record ConsultationState;

// Stan 1: Nowe pytanie, czeka na odpowiedź
public record InitializedConsultation : ConsultationState
{
    public UserQuery Query { get; init; }

    public InitializedConsultation(UserQuery query)
    {
        Query = query ?? throw new ArgumentNullException(nameof(query));
    }
}

// Stan 2: Kiedy jest odpowiedź i źródła
public record AnsweredConsultation : ConsultationState
{
    public UserQuery Query { get; init; }
    public string Response { get; init; }
    public IReadOnlyList<ArticleId> Sources { get; init; }

    public AnsweredConsultation(UserQuery query, string response, IEnumerable<ArticleId> sources)
    {
        Query = query ?? throw new ArgumentNullException(nameof(query));
        Response = response ?? throw new ArgumentNullException(nameof(response));

        if (string.IsNullOrWhiteSpace(response))
            throw new ArgumentException("Odpowiedź asystenta nie może być pusta.", nameof(response));

        Sources = (sources ?? throw new ArgumentNullException(nameof(sources)))
                  .ToList()
                  .AsReadOnly();
    }
}

public class Consultation
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    // powiązanie z użytkownikiem (Type-Safe)
    public UserId CreatedBy { get; private set; }

    // znacznik czasu
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public ConsultationState State { get; private set; }

    // LISTA WIADOMOŚCI (Rdzeń sesji ; Jedno źródło prawdy o sesji)
    private readonly List<Message> _messages = new();
    public IReadOnlyCollection<Message> Messages => _messages.AsReadOnly();

    // Pomocnicze dla kontrolera (opcjonalne - zwraca ostatnie pytanie/odpowiedź)
    public string LastQuestion => _messages.LastOrDefault(m => m.Role == MessageRole.User)?.Content ?? string.Empty;
    public string? LastResponse => _messages.LastOrDefault(m => m.Role == MessageRole.Assistant)?.Content;

    // Zwraca źródła z ostatniej wiadomości asystenta
    public IReadOnlyList<ArticleId> LastSources =>
        _messages.LastOrDefault(m => m.Role == MessageRole.Assistant)?.Sources ?? new List<ArticleId>();

    // Pusty konstruktor - ucisza ostrzeżenia techniczne EF Core
    private Consultation()
    {
        CreatedBy = null!;
        State = null!;
    }

    private Consultation(UserQuery query, UserId userId)
    {
        CreatedBy = userId ?? throw new ArgumentNullException(nameof(userId));
        State = new InitializedConsultation(query);

        // Dodaje pierwsze pytanie do historii
        _messages.Add(new Message(MessageRole.User, query.Text));
    }

    // fabryka - teraz nie da się zacząć konsultacji bez użytkownika
    public static Consultation Start(UserQuery query, UserId userId)
        => new Consultation(query, userId);

    // BEZPIECZNE DODAWANIE ODPOWIEDZI
    public void AddResponse(string response, IEnumerable<ArticleId> sources)
    {
        if (State is not InitializedConsultation initial)
        {
            throw new InvalidOperationException("Nie można dodać odpowiedzi, jeśli nie zadano wcześniej pytania.");
        }

        // Logika biznesowa - dodaje wiadomość asystenta do listy
        var assistantMsg = new Message(MessageRole.Assistant, response, sources);
        _messages.Add(assistantMsg);

        // Zmienia stan na Answered
        State = new AnsweredConsultation(initial.Query, response, sources);
    }

    // BEZPIECZNE DODAWANIE KOLEJNEGO PYTANIA
    public void AddNextQuestion(UserQuery query)
    {
        if (State is InitializedConsultation)
        {
            throw new InvalidOperationException("Nie możesz zadać kolejnego pytania, dopóki nie otrzymasz odpowiedzi na poprzednie.");
        }

        // Dodaje kolejne pytanie użytkownika do tej samej sesji
        _messages.Add(new Message(MessageRole.User, query.Text));

        // Resetuje stan na Initialized (czyli "czekam na odpowiedź")
        State = new InitializedConsultation(query);
    }
}