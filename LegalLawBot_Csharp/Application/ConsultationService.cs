namespace LegalLawBot_Csharp.Application;

using LegalLawBot_Csharp.Domain;
using Telegram.Bot;

public class ConsultationService
{
    private readonly IConsultationRepository _repository;
    private readonly ILegalBrainService _legalBrain;
    private readonly IUserRepository _userRepository;
    private readonly ITelegramBotClient _botClient; // Klient Telegrama do wysyłania powiadomień

    // Dependency Injection - wstrzykuje kontrakty a nie konkretne klasy
    public ConsultationService(
        IConsultationRepository repository,
        ILegalBrainService legalBrain,
        IUserRepository userRepository,
        ITelegramBotClient botClient)
    {
        _repository = repository;
        _legalBrain = legalBrain;
        _userRepository = userRepository;
        _botClient = botClient;
    }

    public async Task<Guid> AskQuestionAsync(UserId userId, string rawQuestion, Guid? existingConsultationId = null)
    {
        // 1. Zamiana prymitywnego stringa na bezpieczny UserQuery
        // Jeśli tekst jest za krótki lub pusty, tu poleci błąd (zgodnie z zasadami w Domain.cs)
        var query = UserQuery.Create(rawQuestion);

        // 1b. LOGIKA BIZNESOWA: Weryfikacja i inkrementacja limitu zapytań użytkownika
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new InvalidOperationException("Nie znaleziono użytkownika o podanym Id.");

        // Weryfikacja limitu - ta metoda z Domain.cs rzuci InvalidOperationException, jeśli DailyQueryCount >= MaxDailyLimit
        user.IncrementQueryCount();

        // 2. Inicjalizacja konsultacji (nowej lub kontynuacja starej)
        Consultation consultation;

        if (existingConsultationId.HasValue)
        {
            var existingConsultation = await _repository.GetByIdAsync(existingConsultationId.Value);

            // Zabezpieczenie: Sprawdza czy sesja istnieje oraz czy należy do tego użytkownika (CreatedBy == userId)
            if (existingConsultation != null && existingConsultation.CreatedBy == userId)
            {
                // PRZYPADEK A: Kontynuacja istniejącej własnej sesji
                consultation = existingConsultation;
                // Dodaje kolejne pytanie do istniejącego agregatu
                consultation.AddNextQuestion(query);
            }
            else
            {
                // SAMONAPRAWA (Self-Healing) + BEZPIECZEŃSTWO (Sesja nie istnieje LUB należy do innego użytkownika!)
                // ID sesji istniało w profilu Usera, ale sesji nie ma już w bazie (np. została usunięta)
                // Czyści zły wskaźnik i płynnie tworzy nową sesję bez rzucania błędem
                user.ClearActiveConsultation();
                consultation = Consultation.Start(query, userId);
            }
        }
        else
        {
            // PRZYPADEK B: Start nowej sesji
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

        // 5. Zapisanie efektu pracy w repozytorium konsultacji
        if (existingConsultationId.HasValue && consultation.Id == existingConsultationId.Value)
        {
            await _repository.UpdateAsync(consultation);
        }
        else
        {
            await _repository.AddAsync(consultation);
        }

        // 5b. Zapisanie zaktualizowanego licznika zapytań użytkownika w bazie danych
        await _userRepository.UpdateAsync(user);

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
            c.Title // Pobiera bezpośrednio właściwość Title z bazy //
        ));
    }

    // Pobiera pełną historię jednej sesji z weryfikacją właściciela
    public async Task<ConsultationDetailsDto?> GetConsultationDetailsAsync(Guid id, UserId userId)
    {
        var consultation = await _repository.GetByIdAsync(id);

        // Jeśli konsultacja nie istnieje LUB należy do innego użytkownika -> zwraca null
        if (consultation == null || consultation.CreatedBy != userId)
            return null;

        var history = consultation.Messages
            .OrderBy(m => m.CreatedAt) // Układa wiadomości od najstarszej do najnowszej
            .Select(m => new ChatMessageDto(
                m.Role.ToString().ToLower(),
                m.Content,
                m.Sources.Select(s => s.Value).ToList()
            )).ToList();

        return new ConsultationDetailsDto(consultation.Id, consultation.CreatedAt, history);
    }

    // Usuwa wskazaną sesję wraz z historią - DELETE
    // Usunięcie sesji jest razem z obsługą czyszczenia User.ActiveConsultationId oraz powiadomieniem na Telegram
    public async Task<bool> DeleteConsultationAsync(Guid id)
    {
        var consultation = await _repository.GetByIdAsync(id);
        if (consultation == null) return false;

        // Pobiera właściciela konsultacji
        var user = await _userRepository.GetByIdAsync(consultation.CreatedBy);

        if (user != null && user.ActiveConsultationId == id)
        {
            // Jeśli usuwana konsultacja była aktywna to czyści profil użytkownika
            user.ClearActiveConsultation();
            await _userRepository.UpdateAsync(user);

            // Jeśli użytkownik ma sparowany Telegram to wysyła powiadomienie
            if (user.TelegramChatId != null)
            {
                try
                {
                    await _botClient.SendMessage(
                        chatId: user.TelegramChatId.Value,
                        text: "Kontekst rozmowy został wyczyszczony z poziomu przeglądarki, a sesja usunięta! 🧹 Możemy zaczynać od nowa. O co chcesz zapytać?"
                    );
                }
                catch (Exception ex)
                {
                    // Loguje ewentualny błąd wysyłki (np. jeśli użytkownik zablokował bota)
                    Console.WriteLine($"[Telegram Sync Error]: {ex.Message}");
                }
            }
        }

        // Usunięcie konsultacji i powiązanych wiadomości z bazy
        await _repository.DeleteAsync(consultation);
        return true;
    }
    // Zmiana tytułu konsultacji - PATCH
    public async Task<bool> UpdateTitleAsync(Guid id, string newTitle)
    {
        var consultation = await _repository.GetByIdAsync(id);
        if (consultation == null) return false;

        // Wywołuje bezpieczną metodę biznesową z encji (tam jest walidacja)
        consultation.UpdateTitle(newTitle);

        // Zapisuje zmiany w bazie przez repozytorium
        await _repository.UpdateAsync(consultation);
        return true;
    }
    // Wyciąga treść i źródła ostatniej odpowiedzi dla Frontendu
    public async Task<(string Answer, List<string> Sources)> GetLatestAnswerAsync(Guid consultationId)
    {
        var consultation = await _repository.GetByIdAsync(consultationId);
        var lastAssistantMessage = consultation?.Messages
            .OrderBy(m => m.CreatedAt)
            .LastOrDefault(m => m.Role.ToString().Equals("Assistant", StringComparison.OrdinalIgnoreCase));

        var answer = lastAssistantMessage?.Content ?? "";
        var sources = lastAssistantMessage?.Sources.Select(s => s.Value).ToList() ?? new List<string>();

        return (answer, sources);
    }
}

public record ConsultationSummaryDto(Guid Id, DateTime CreatedAt, string Title);

public record ConsultationDetailsDto(Guid Id, DateTime CreatedAt, List<ChatMessageDto> History);