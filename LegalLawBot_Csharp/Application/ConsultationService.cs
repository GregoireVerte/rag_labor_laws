namespace LegalLawBot_Csharp.Application;

using LegalLawBot_Csharp.Domain;

public class ConsultationService
{
    private readonly IConsultationRepository _repository;
    private readonly ILegalBrainService _legalBrain;

    // Dependency Injection - wstrzykuje kontrakty a nie konkretne klasy
    public ConsultationService(IConsultationRepository repository, ILegalBrainService legalBrain)
    {
        _repository = repository;
        _legalBrain = legalBrain;
    }

    public async Task<Guid> AskQuestionAsync(UserId userId, string rawQuestion)
    {
        // 1. Zamiana prymitywnego stringa na bezpieczny UserQuery
        // Jeśli tekst jest za krótki lub pusty, tu poleci błąd (zgodnie z zasadami w Domain.cs)
        var query = UserQuery.Create(rawQuestion);

        // 2. Inicjalizacja nowej konsultacji
        var consultation = Consultation.Start(query, userId);

        // 3. Wywołanie "Mózgu" w Pythonie przez interfejs
        // Aplikacja nie wie, że to leci na serwer Render - ona tylko prosi o odpowiedź
        var (answer, sources) = await _legalBrain.AskLegalQuestionAsync(query);

        // 4. Przejście do stanu AnsweredConsultation (logika domenowa)
        consultation.AddResponse(answer, sources);

        // 5. Zapisanie efektu pracy w repozytorium
        await _repository.AddAsync(consultation);

        // Zwraca Id, żeby frontend mógł później o tę konsultację zapytać
        return consultation.Id;
    }
}