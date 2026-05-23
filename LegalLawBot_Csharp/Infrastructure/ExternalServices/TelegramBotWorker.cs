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
    private TelegramBotClient? _botClient;

    public TelegramBotWorker(ILogger<TelegramBotWorker> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
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
        _logger.LogInformation("Otrzymano nową wiadomość w tle!");
        await Task.CompletedTask;
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Wystąpił błąd podczas nasłuchiwania Telegram API.");
        return Task.CompletedTask;
    }
}