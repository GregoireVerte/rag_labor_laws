using Microsoft.AspNetCore.Mvc;
using LegalLawBot_Csharp.Application;
using LegalLawBot_Csharp.Domain;

namespace LegalLawBot_Csharp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConsultationController : ControllerBase
{
    private readonly ConsultationService _consultationService;
    private readonly IUserRepository _userRepository;

    public ConsultationController(ConsultationService consultationService, IUserRepository userRepository)
    {
        _consultationService = consultationService;
        _userRepository = userRepository;
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskRequest request)
    {
        try
        {
            // Tworzy tymczasowe ID użytkownika (do zastąpienia przez ID zalogowanej osoby)
            // var userId = UserId.Create(Guid.NewGuid());

            // Używa tego samego stałego ID co w GetAll, żeby widzieć wyniki w testach
            var userId = UserId.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));

            // Weryfikacja tożsamości w bazie danych
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
            {
                return Unauthorized("Twój profil użytkownika nie został znaleziony w systemie.");
            }

            // Przekazuje zadanie do orkiestratora -> Application Layer ; z opcjonalnym ID sesji
            var consultationId = await _consultationService.AskQuestionAsync(userId, request.Question, request.ConsultationId);

            // Zwraca info o sukcesie i ID nowej konsultacji
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
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        // Na razie używa stałego ID (później do zastąpienia Admin ID)
        var userId = UserId.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));

        // Weryfikacja tożsamości w bazie danych przed pobraniem sesji
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            return Unauthorized("Brak uprawnień do przeglądania tej listy sesji.");
        }

        var sessions = await _consultationService.GetUserConsultationsAsync(userId);
        return Ok(sessions);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var details = await _consultationService.GetConsultationDetailsAsync(id);
        if (details == null) return NotFound("Nie znaleziono takiej konsultacji.");

        return Ok(details);
    }

    [HttpDelete("{id}")]
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
}

// Prosty model (DTO) do odebrania pytania z JSONa; dodany opcjonalny parametr ConsultationId //
public record AskRequest(string Question, Guid? ConsultationId = null);

public record UpdateTitleRequest(string Title);