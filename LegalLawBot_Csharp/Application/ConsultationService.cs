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

    public async Task<Guid> AskQuestionAsync(UserId userId, string rawQuestion, Guid? existingConsultationId = null)
    {
        // 1. Zamiana prymitywnego stringa na bezpieczny UserQuery
        // Jeśli tekst jest za krótki lub pusty, tu poleci błąd (zgodnie z zasadami w Domain.cs)
        var query = UserQuery.Create(rawQuestion);

        // 2. Inicjalizacja konsultacji (nowej lub kontynuacja starej)
        Consultation consultation;

        if (existingConsultationId.HasValue)
        {
            // PRZYPADEK A: Kontynuacja rozmowy
            consultation = await _repository.GetByIdAsync(existingConsultationId.Value)
                ?? throw new InvalidOperationException("Nie znaleziono sesji o podanym Id.");

            // Dodaje kolejne pytanie do istniejącego agregatu
            consultation.AddNextQuestion(query);
        }
        else
        {
            // PRZYPADEK B: Startuje nową sesję
            consultation = Consultation.Start(query, userId);
        }

        // 3. Wywołanie "Mózgu" w Pythonie przez interfejs ; przygotowanie historii rozmowy dla Pythona
        // Aplikacja nie wie, że to leci na serwer Render - ona tylko prosi o odpowiedź
        // Pobiera dotychczasowe wiadomości i mapuje na format DTO
        var historyDto = consultation.Messages
            .Select(m => new ChatMessageDto(
                m.Role.ToString().ToLower(), // Zamienia "Assistant" na "assistant"
                m.Content))
            .ToList();

        // wywołanie mózgu z pełną historią
        var (answer, sources) = await _legalBrain.AskLegalQuestionAsync(query, historyDto);

        // 4. Dodanie odpowiedzi (niezależnie czy nowa, czy stara sesja)
        consultation.AddResponse(answer, sources);

        // 5. Zapisanie efektu pracy w repozytorium
        if (existingConsultationId.HasValue)
        {
            await _repository.UpdateAsync(consultation);
        }
        else
        {
            await _repository.AddAsync(consultation);
        }

        // Zwraca Id, żeby frontend mógł później o tę konsultację zapytać
        return consultation.Id;
    }
    // Pobiera listę wszystkich sesji dla danego użytkownika
    public async Task<IEnumerable<ConsultationSummaryDto>> GetUserConsultationsAsync(UserId userId)
    {
        var consultations = await _repository.GetByUserIdAsync(userId);

        return consultations.Select(c => new ConsultationSummaryDto(
            c.Id,
            c.CreatedAt,
            c.Messages.FirstOrDefault()?.Content ?? "Nowa konsultacja"
        ));
    }

    // Pobiera pełną historię jednej sesji
    public async Task<ConsultationDetailsDto?> GetConsultationDetailsAsync(Guid id)
    {
        var consultation = await _repository.GetByIdAsync(id);
        if (consultation == null) return null;

        var history = consultation.Messages.Select(m => new ChatMessageDto(
            m.Role.ToString().ToLower(),
            m.Content
        )).ToList();

        return new ConsultationDetailsDto(consultation.Id, consultation.CreatedAt, history);
    }

    // Usuwa wskazaną sesję wraz z historią - DELETE
    public async Task<bool> DeleteConsultationAsync(Guid id)
    {
        var consultation = await _repository.GetByIdAsync(id);
        if (consultation == null) return false;

        await _repository.DeleteAsync(consultation);
        return true;
    }
}

public record ConsultationSummaryDto(Guid Id, DateTime CreatedAt, string FirstQuestion);

public record ConsultationDetailsDto(Guid Id, DateTime CreatedAt, List<ChatMessageDto> History);