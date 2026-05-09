using Microsoft.AspNetCore.Mvc;
using LegalLawBot_Csharp.Application;
using LegalLawBot_Csharp.Domain;

namespace LegalLawBot_Csharp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConsultationController : ControllerBase
{
    private readonly ConsultationService _consultationService;

    public ConsultationController(ConsultationService consultationService)
    {
        _consultationService = consultationService;
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskRequest request)
    {
        try
        {
            // 1. Tworzy tymczasowe ID użytkownika (do zastąpienia przez ID zalogowanej osoby)
            var userId = UserId.Create(Guid.NewGuid());

            // 2. Przekazuje zadanie do orkiestratora -> Application Layer ; z opcjonalnym ID sesji
            var consultationId = await _consultationService.AskQuestionAsync(userId, request.Question, request.ConsultationId);

            // 3. Zwraca info o sukcesie i ID nowej konsultacji
            return Ok(new { Message = "Konsultacja zakończona sukcesem!", Id = consultationId });
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
}

// Prosty model (DTO) do odebrania pytania z JSONa; dodany opcjonalny parametr ConsultationId //
public record AskRequest(string Question, Guid? ConsultationId = null);