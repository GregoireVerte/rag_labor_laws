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
    public Guid Id { get; } = Guid.NewGuid();
    public ConsultationState State { get; private set; }

    private Consultation(UserQuery query)
    {
        State = new InitializedConsultation(query);
    }

    public static Consultation Start(UserQuery query) => new(query);

    public void AddResponse(string response, IEnumerable<ArticleId> sources)
    {
        if (State is InitializedConsultation initial)
        {
            State = new AnsweredConsultation(initial.Query, response, sources);
        }
        else
        {
            throw new InvalidOperationException("Nie można dodać odpowiedzi do już obsłużonej konsultacji.");
        }
    }
}