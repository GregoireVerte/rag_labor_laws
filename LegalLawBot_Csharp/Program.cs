using LegalLawBot_Csharp.Application;
using LegalLawBot_Csharp.Domain;
using LegalLawBot_Csharp.Infrastructure.ExternalServices;
using LegalLawBot_Csharp.Infrastructure.Persistence;
// using LegalLawBot_Csharp.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

// 1. Rejestracja ConsultationService (Warstwa Aplikacji)
builder.Services.AddScoped<ConsultationService>();

// 2. Konfiguracja połączenia z Pythonem na Renderze
builder.Services.AddHttpClient<ILegalBrainService, LegalBrainServiceClient>(client =>
{
    // ADRES Z RENDERA (Publiczny bo darmowy Render blokuje ruch internal przychodzący)
    client.BaseAddress = new Uri("https://rag-labor-laws-backend.onrender.com/");

    // Zwiększenie czasu do 5 minut, żeby przeżyć wybudzanie darmowego Rendera
    client.Timeout = TimeSpan.FromMinutes(5);

    // Dodanie User-Agent'a żeby zapora Rendera nie blokowała aplikacji jako bota
    // TryAddWithoutValidation ignoruje rygorystyczne testy formatu .NET i przesyła czysty tekst
    client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");
});

// 3. Rejestracja Repozytorium (Prawdziwe - EF Core)
// Teraz aplikacja będzie zapisywać dane w Supabase zamiast w pamięci RAM
builder.Services.AddScoped<IConsultationRepository, EfConsultationRepository>();
builder.Services.AddScoped<IUserRepository, EfUserRepository>();

// Rejestracja klienta Telegrama (wczytuje token ze zmiennych środowiskowych)
var token = builder.Configuration["TelegramBot:Token"]
            ?? Environment.GetEnvironmentVariable("TelegramBot__Token")
            ?? throw new InvalidOperationException("Brak tokenu bota Telegrama w konfiguracji serwera!");
builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(token));

// Add services to the container
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi

// Pobiera adres bazy z pliku appsettings.json
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Rejestruje DbContext z użyciem PostgreSQL
builder.Services.AddDbContext<LegalLawBotDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        // prawdziwy adres z Vercel obok portów deweloperskich
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000", "https://rag-labor-laws.vercel.app")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors("AllowFrontend");

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// Przechwytywanie dynamicznego portu dla serwera Render
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    // Nakazuje .NET słuchać na porcie przydzielonym przez Render pod adresem 0.0.0.0
    app.Urls.Add($"http://0.0.0.0:{port}");
}

app.Run();
