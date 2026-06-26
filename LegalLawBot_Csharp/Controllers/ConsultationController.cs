using Microsoft.AspNetCore.Mvc;
using LegalLawBot_Csharp.Application;
using LegalLawBot_Csharp.Domain;
using Telegram.Bot;
using Telegram.Bot.Types;
using Microsoft.Extensions.DependencyInjection;

namespace LegalLawBot_Csharp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConsultationController : ControllerBase
{
    private readonly ConsultationService _consultationService;
    private readonly IUserRepository _userRepository;
    private readonly IServiceScopeFactory _scopeFactory; // Fabryka do bezpiecznych wątków w tle
    private readonly ITelegramBotClient _botClient;       // Klient Telegrama do odsyłania odpowiedzi

    public ConsultationController(
        ConsultationService consultationService,
        IUserRepository userRepository,
        IServiceScopeFactory scopeFactory,
        ITelegramBotClient botClient)
    {
        _consultationService = consultationService;
        _userRepository = userRepository;
        _scopeFactory = scopeFactory;
        _botClient = botClient;
    }

    // ENDPOINT: Bramka dla Webhooka od Telegrama
    [HttpPost("/webhook/telegram")]
    public IActionResult TelegramWebhook([FromBody] Update update)
    {
        // Sprawdza czy to zwykła wiadomość tekstowa
        if (update.Message is not { Text: { } messageText } message)
            return Ok(); // Jeśli to edycja posta lub załącznik, ignoruje, ale daje 200 OK

        var chatId = message.Chat.Id;
        var firstName = message.Chat.FirstName ?? "Użytkownik";

        // Odpowiada Telegramowi w ułamku sekundy że odebrało przesyłkę
        // Całą logikę biznesową wrzuca do odseparowanego zadania w tle (Fire-and-Forget)
        _ = Task.Run(async () =>
        {
            // Ponieważ ten kod działa w tle trzeba ręcznie otworzyć nowy "Scope" dla bazy danych
            using var scope = _scopeFactory.CreateScope();
            var scopedUserService = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var scopedConsultationService = scope.ServiceProvider.GetRequiredService<ConsultationService>();

            try
            {
                // Najpierw wysyła komunikat. Telegram dostaje informację, Bot natychmiast odpowiada
                await _botClient.SendMessage(chatId, "Przeszukuję bazę wiedzy prawa pracy... 🔍 Proszę o chwilę cierpliwości. (Inicjalizacja serwera AI, to może potrwać około minuty...)");

                // Przedskoczek w bezpiecznym "izolatorze" try/catch ; z pełnym oczekiwaniem (AWAIT) – pętla sprawdzająca stan Pythona
                try
                {
                    using (var wakeUpClient = new HttpClient())
                    {
                        wakeUpClient.Timeout = TimeSpan.FromSeconds(10); // Krótki timeout na pojedynczy strzał

                        // Używa bezpiecznej metody zapisu nagłówka z TryAddWithoutValidation
                        wakeUpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");

                        // Sygnał GET na publiczny adres - w pętli i z użyciem AWAIT

                        bool pythonIsReady = false;
                        int attempts = 0;

                        // Próbuje maks 20 razy (20 * minimum 4 sekundy to minimum 80 sekund oczekiwania z jitter'em)
                        while (!pythonIsReady && attempts < 20)
                        {
                            attempts++;
                            try
                            {
                                // uderza dokładnie w ścieżkę API zamiast w stronę główną
                                var response = await wakeUpClient.GetAsync("https://rag-labor-laws-backend.onrender.com/api/v1/legal-brain/ask");
                                var mediaType = response.Content.Headers.ContentType?.MediaType;

                                // Python działa tylko wtedy gdy bramka przepuściła ruch i odpowiedziało kodem FastAPI (zwracając JSON)
                                // upewnia się że status kod nie jest maszynowym 429 od firewalla Rendera
                                if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests &&
                                    mediaType != null &&
                                    mediaType.Contains("application/json"))
                                {
                                    pythonIsReady = true;
                                }
                                else
                                {
                                    // Serwer wciąż się budzi (Gdy Render zwraca ekran ładowania i wciąż dostajemy HTML) lub Text/Plain (błąd 429) – czeka od 4 do 6 sekund przed kolejną próbą
                                    await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(4000, 6001)));
                                }
                            }
                            catch
                            {
                                // W razie błędów sieciowych serwera podczas wstawania kontenera również cierpliwie czeka losowo 4-6 sekund
                                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(4000, 6001)));
                            }
                        }
                    }
                }
                catch (Exception wakeUpEx)
                {
                    // Jeśli przedskoczek zawiedzie tylko loguje to w konsoli. Nie wywala całej aplikacji
                    Console.WriteLine($"[WakeUp Loop Critical Failure]: {wakeUpEx.Message}");
                }

                // Klasyczna logika biznesowa (pobieranie usera i wysyłanie pytania do publicznego API)
                // Szuka użytkownika po jego ChatId z Telegrama
                var telegramChatId = TelegramChatId.Create(chatId);
                var user = await scopedUserService.GetByTelegramChatIdAsync(telegramChatId);

                if (user == null)
                {
                    await _botClient.SendMessage(chatId, "Twój profil na Telegramie nie jest powiązany z żadnym kontem w systemie.");
                    return;
                }

                // Wywołuje dokładnie tę samą logikę biznesową (DDD) co zawsze
                var consultationId = await scopedConsultationService.AskQuestionAsync(
                    user.Id,
                    messageText,
                    user.ActiveConsultationId
                );

                // Pobiera odpowiedź i źródła
                var (answer, sources) = await scopedConsultationService.GetLatestAnswerAsync(consultationId);

                // Formuje wiadomość i wysyła do użytkownika
                var responseText = $"{answer}\n\nŹródła:\n" + string.Join("\n", sources);
                await _botClient.SendMessage(chatId, responseText);
            }
            catch (Exception ex)
            {
                // Loguje błąd w tle i powiadamia użytkownika
                Console.WriteLine($"[Webhook Error]: {ex.Message}");
                await _botClient.SendMessage(chatId, "Przepraszam, wystąpił problem techniczny po stronie serwera AI.");
            }
        });

        // Zwraca status 200 OK natychmiast do Telegrama
        return Ok();
    }

    // Było: [HttpPost("ask")]
    [HttpPost("/ask")]
    public async Task<IActionResult> Ask([FromBody] AskRequest request)
    {
        try
        {
            // dynamiczne pobieranie ID przesłanego z frontendu:
            var userId = UserId.Create(request.UserId);

            // Weryfikacja tożsamości w bazie danych
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
            {
                return Unauthorized("Twój profil użytkownika nie został znaleziony w systemie.");
            }

            // Przekazuje zadanie do orkiestratora -> Application Layer ; z opcjonalnym ID sesji
            var consultationId = await _consultationService.AskQuestionAsync(userId, request.Question, request.ConsultationId);

            // Pobiera dokładnie to, czego oczekuje stary Frontend
            var (answer, sources) = await _consultationService.GetLatestAnswerAsync(consultationId);

            // Zwraca format idealny dla Reacta dorzucając ID sesji
            return Ok(new { answer = answer, sources = sources, id = consultationId });
        }
        catch (ArgumentException ex)
        {
            // Jeśli np. pytanie było za krótkie (walidacja jest w Domain) to zwraca błąd 400
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            // W razie nieprzewidzianych błędów (np. problem z połączeniem z Pythonem)
            return StatusCode(500, $"Błąd serwera: {ex.Message}");
        }
    }
    // Było: [HttpGet]
    [HttpGet("/sessions")]
    public async Task<IActionResult> GetAll([FromQuery] Guid userId)
    {
        // wcześniejszy sztywny GUID Admina został zastąpiony parametrem z zapytania
        var domainUserId = UserId.Create(userId);

        // Weryfikacja tożsamości w bazie danych przed pobraniem sesji
        var user = await _userRepository.GetByIdAsync(domainUserId);
        if (user == null)
        {
            return Unauthorized("Brak uprawnień do przeglądania tej listy sesji.");
        }

        var sessions = await _consultationService.GetUserConsultationsAsync(domainUserId);
        return Ok(sessions);
    }

    // Było: [HttpGet("{id}")]
    [HttpGet("/history/{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var details = await _consultationService.GetConsultationDetailsAsync(id);
        if (details == null) return NotFound("Nie znaleziono takiej konsultacji.");

        return Ok(details);
    }

    // Było: [HttpDelete("{id}")]
    [HttpDelete("/sessions/{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _consultationService.DeleteConsultationAsync(id);
        if (!deleted) return NotFound("Nie znaleziono konsultacji o podanym Id.");

        return Ok(new { Message = "Konsultacja została pomyślnie usunięta." });
    }
    [HttpPatch("{id}/title")]
    public async Task<IActionResult> UpdateTitle(Guid id, [FromBody] UpdateTitleRequest request)
    {
        try
        {
            var updated = await _consultationService.UpdateTitleAsync(id, request.Title);
            if (!updated) return NotFound("Nie znaleziono konsultacji o podanym Id.");

            return Ok(new { Message = "Tytuł konsultacji został pomyślnie zmieniony." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Błąd serwera: {ex.Message}");
        }
    }
    [HttpPost("admin/link-telegram")]
    public async Task<IActionResult> LinkAdminTelegram([FromBody] LinkTelegramRequest request)
    {
        try
        {
            // 1. Pobiera dynamiczne ID Admina przekazane w body
            var adminId = UserId.Create(request.AdminId);
            var user = await _userRepository.GetByIdAsync(adminId);

            if (user == null)
                return NotFound("Nie znaleziono profilu administratora w bazie.");

            // 2. Tworzy obiekt wartości (tu odpali się ewentualna walidacja)
            var telegramId = TelegramChatId.Create(request.ChatId);

            // 3. Wywołuje czystą metodę biznesową z domeny (DDD)
            user.LinkTelegram(telegramId);

            // 4. Zapisuje zaktualizowanego użytkownika w Supabase przez repozytorium
            await _userRepository.UpdateAsync(user);

            return Ok(new { Message = $"Pomyślnie powiązano konto administratora z Telegram Chat ID: {request.ChatId}" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Błąd serwera: {ex.Message}");
        }
    }
}

// Prosty model (DTO) do odebrania pytania z JSONa; dodany opcjonalny parametr ConsultationId //
// Użycie JsonPropertyName, żeby .NET wiedział, że "session_id" z Reacta to "ConsultationId"
public record AskRequest(
    string Question,
    [property: System.Text.Json.Serialization.JsonPropertyName("user_id")] Guid UserId,
    [property: System.Text.Json.Serialization.JsonPropertyName("session_id")] Guid? ConsultationId = null
);

public record UpdateTitleRequest(string Title);
public record LinkTelegramRequest(
    long ChatId,
    [property: System.Text.Json.Serialization.JsonPropertyName("user_id")] Guid AdminId
);