using LegalLawBot_Csharp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LegalLawBot_Csharp.Infrastructure.BackgroundServices;

public class DailyLimitResetService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailyLimitResetService> _logger;

    public DailyLimitResetService(IServiceScopeFactory scopeFactory, ILogger<DailyLimitResetService> _logger)
    {
        _scopeFactory = scopeFactory;
        this._logger = _logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // 1. Pobiera strefę czasową dla Polski (obsługuje automatycznie CET/CEST)
            var polandTz = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
            var nowInPoland = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, polandTz);

            // 2. Oblicza najbliższą północ w Polsce
            var nextMidnightInPoland = nowInPoland.Date.AddDays(1);
            var delay = nextMidnightInPoland - nowInPoland;

            // Zabezpieczenie przed ułamkami sekund
            if (delay.TotalMilliseconds <= 0)
            {
                delay = TimeSpan.FromDays(1);
            }

            _logger.LogInformation("Robot limitów zasypia na {Delay} do najbliższej północy w Polsce.", delay);

            // 3. Czeka do północy
            await Task.Delay(delay, stoppingToken);

            // 4. Wybija północ -> odpala zerowanie w bezpiecznym Scope bazy danych
            try
            {
                _logger.LogInformation("W Polsce wybiła północ. Uruchamiam automatyczne zerowanie liczników zapytań...");

                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<LegalLawBotDbContext>();

                // Wykonuje bezpośredni UPDATE w bazie bez pobierania użytkowników do pamięci RAM
                int updatedRows = await dbContext.Users
                    .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.DailyQueryCount, 0), stoppingToken);

                _logger.LogInformation("Liczniki zapytań zostały zresetowane. Zaktualizowano użytkowników: {Count}.", updatedRows);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wystąpił błąd podczas nocnego zerowania liczników zapytań.");
            }
        }
    }
}