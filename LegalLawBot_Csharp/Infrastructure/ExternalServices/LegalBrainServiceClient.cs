using System.Net.Http.Json;
using LegalLawBot_Csharp.Domain;

namespace LegalLawBot_Csharp.Infrastructure.ExternalServices;

public class LegalBrainServiceClient : ILegalBrainService
{
    private readonly HttpClient _httpClient;

    public LegalBrainServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<(string Response, IEnumerable<ArticleId> Sources)> AskLegalQuestionAsync(UserQuery query)
    {
        // 1. Przygotowanie danych do wysłania (zgodnie z modelem Query w Pythonie)
        var payload = new { question = query.Text };

        // 2. Wysłanie zapytania do Pythona na Renderze
        // (Adres URL skonfigurowany w Program.cs)
        var response = await _httpClient.PostAsJsonAsync("api/v1/legal-brain/ask", payload);
        response.EnsureSuccessStatusCode();

        // 3. Odebranie wyniku
        var result = await response.Content.ReadFromJsonAsync<LegalBrainResponse>();

        // 4. Mapowanie stringów z Pythona na bezpieczne ArticleId z Domain
        var sources = result!.Sources.Select(ArticleId.Create);

        return (result.Answer, sources);
    }

    // Pomocnicza klasa do odczytu JSONa z Pythona
    private record LegalBrainResponse(string Answer, List<string> Sources);
}