using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace LegalLawBot_Csharp.Infrastructure.ExternalServices;

public class TelegramBotWorker : BackgroundService
{
    private readonly ILogger<TelegramBotWorker> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private TelegramBotClient? _botClient;

    // KONSTRUKTOR
    public TelegramBotWorker(
        ILogger<TelegramBotWorker> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 1. Pobiera bezpiecznie ukryty token z User Secrets
        var token = _configuration["TelegramBot:Token"];
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogError("Brak tokenu bota Telegrama w konfiguracji (User Secrets)!");
            return;
        }

        // 2. Inicjalizuje klienta Telegrama
        _botClient = new TelegramBotClient(token);

        // Pobiera info o bocie by upewnić się że token działa
        var me = await _botClient.GetMe(cancellationToken: stoppingToken);
        _logger.LogInformation("Uruchomiono bota Telegrama! Nazwa: {BotName}, Username: @{BotUsername}", me.FirstName, me.Username);

        // Automatyczne czyszczenie starego webhooka w chmurze Telegrama (naprawia błąd 409)
        await _botClient.DeleteWebhook(cancellationToken: stoppingToken);

        // 3. Konfiguracja opcji odbierania wiadomości
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message } // Słucha tylko wiadomości tekstowych
        };

        // 4. Odpala pętlę nasłuchiwania
        _botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken
        );

        // Utrzymuje serwis przy życiu dopóki aplikacja działa
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        // 1. Sprawdza czy otrzymany pakiet to na pewno nowa wiadomość tekstowa
        if (update.Message is not { Text: { } messageText } message)
            return;

        // wyciąga dane: unikalny numer czatu (Chat ID) oraz imię nadawcy
        var chatId = message.Chat.Id;
        var username = message.From?.FirstName ?? "Nieznajomy";

        _logger.LogInformation("Bot otrzymał wiadomość od {Name} (ChatID: {Id}): '{Text}'", username, chatId, messageText);

        // 2. Otwiera tymczasową furtkę (Scope) dla usług Scoped (baza danych)
        using (var scope = _serviceProvider.CreateScope())
        {
            // 3. Wyciąga repozytorium użytkowników bezpośrednio z tej furtki
            var userRepository = scope.ServiceProvider.GetRequiredService<Domain.IUserRepository>();

            // 4. Szuka użytkownika w Supabase po jego Telegram Chat ID
            var telegramChatId = Domain.TelegramChatId.Create(chatId);
            var user = await userRepository.GetByTelegramChatIdAsync(telegramChatId);

            // 5. Ochroniarz: jeśli bota zaczepi ktoś nieznajomy odmawia dostępu
            if (user == null)
            {
                _logger.LogWarning("Odmowa dostępu dla ChatID: {Id} (Brak w bazie)", chatId);
                await botClient.SendMessage(
                    chatId: chatId,
                    text: "Dostęp zablokowany. Twój identyfikator Telegram nie jest zarejestrowany w systemie Asystenta Prawa Pracy.",
                    cancellationToken: cancellationToken
                );
                return;
            }

            // 6.Użytkownik autoryzowany –> odpala RAG
            // ponieważ serwer Pythona na Renderze potrzebuje kilku sekund na analizę
            // najpierw wysyła użytkownikowi sygnał że system podjął pracę
            await botClient.SendMessage(
                chatId: chatId,
                text: "Przeszukuję bazę wiedzy prawa pracy... ⏳ Proszę o chwilę cierpliwości. ⏳",
                cancellationToken: cancellationToken
            );

            // wyciąga ConsultationService z tymczasowej "furtki" (scope)
            var consultationService = scope.ServiceProvider.GetRequiredService<Application.ConsultationService>();

            // Wywołuje pełny proces biznesowy:
            // Tworzy nową sesję, zapisuje ją w Supabase, odpytuje Pythona na Renderze, dołącza artykuły i zapisuje odpowiedź
            var consultationId = await consultationService.AskQuestionAsync(user.Id, messageText);

            // Pobiera szczegóły tej nowo utworzonej sesji, żeby wyciągnąć treść odpowiedzi AI
            var details = await consultationService.GetConsultationDetailsAsync(consultationId);

            // Ostatnia wiadomość w historii to odpowiedź asystenta (z małej litery "content")
            var aiResponse = details?.History.LastOrDefault()?.content
                             ?? "Przepraszam, wystąpił problem podczas pobierania odpowiedzi z bazy wiedzy.";

            // Odsyła ostateczną treść merytoryczną bezpośrednio użytkownikowi na Telegram
            await botClient.SendMessage(
                chatId: chatId,
                text: aiResponse,
                cancellationToken: cancellationToken
            );
        }
        // w tym miejscu furtka się zamyka – repozytorium i połączenie z bazy danych (Supabase) są niszczone w pamięci
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Wystąpił błąd podczas nasłuchiwania Telegram API.");
        return Task.CompletedTask;
    }
}