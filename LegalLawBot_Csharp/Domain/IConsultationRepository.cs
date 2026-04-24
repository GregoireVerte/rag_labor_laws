namespace LegalLawBot_Csharp.Domain;

// Interfejs to lista życzeń domeny // definiuje co chcemy zrobić a nie jak (implementacja będzie w Infrastructure) //
public interface IConsultationRepository
{
    // Pobiera konsultację po jej Id
    Task<Consultation?> GetByIdAsync(Guid id);

    // Pobiera wszystkie konsultacje danego użytkownika
    Task<IEnumerable<Consultation>> GetByUserIdAsync(UserId userId);

    // Zapisuje nową lub zaktualizowaną konsultację
    Task AddAsync(Consultation consultation);

    // Aktualizuje istniejącą (np. gdy dojdzie odpowiedź z Pythona)
    Task UpdateAsync(Consultation consultation);
}