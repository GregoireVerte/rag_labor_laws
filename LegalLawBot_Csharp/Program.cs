using LegalLawBot_Csharp.Application;
using LegalLawBot_Csharp.Domain;
using LegalLawBot_Csharp.Infrastructure.ExternalServices;
using LegalLawBot_Csharp.Infrastructure.Persistence;
// using LegalLawBot_Csharp.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Rejestracja ConsultationService (Warstwa Aplikacji)
builder.Services.AddScoped<ConsultationService>();

// 2. Konfiguracja połączenia z Pythonem na Renderze
builder.Services.AddHttpClient<ILegalBrainService, LegalBrainServiceClient>(client =>
{
    // ADRES Z RENDERA
    client.BaseAddress = new Uri("https://rag-labor-laws-backend.onrender.com/");

    // Zwiększenie czasu do 5 minut, żeby przeżyć wybudzanie darmowego Rendera
    client.Timeout = TimeSpan.FromMinutes(5);
});

// 3. Rejestracja Repozytorium (Prawdziwe - EF Core)
// Teraz aplikacja będzie zapisywać dane w Supabase zamiast w pamięci RAM
builder.Services.AddScoped<IConsultationRepository, EfConsultationRepository>();
builder.Services.AddScoped<IUserRepository, EfUserRepository>();

// 4. Rejestracja bota Telegrama jako serwisu w tle
builder.Services.AddHostedService<TelegramBotWorker>();

// Add services to the container.

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
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000") // porty Reacta
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

app.Run();
