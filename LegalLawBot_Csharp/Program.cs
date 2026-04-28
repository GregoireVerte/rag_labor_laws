using LegalLawBot_Csharp.Application;
using LegalLawBot_Csharp.Domain;
using LegalLawBot_Csharp.Infrastructure.ExternalServices;
using LegalLawBot_Csharp.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// 1. Rejestracja ConsultationService (Warstwa Aplikacji)
builder.Services.AddScoped<ConsultationService>();

// 2. Konfiguracja połączenia z Pythonem na Renderze
builder.Services.AddHttpClient<ILegalBrainService, LegalBrainServiceClient>(client =>
{
    // ADRES Z RENDERA
    client.BaseAddress = new Uri("https://rag-labor-laws-backend.onrender.com/");
});

// 3. Rejestracja Repozytorium (na razie "fake" dopóki nie podepniemy bazy)
// To pozwoli uruchomić projekt bez błędów kompilacji
builder.Services.AddScoped<IConsultationRepository, FakeConsultationRepository>();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
