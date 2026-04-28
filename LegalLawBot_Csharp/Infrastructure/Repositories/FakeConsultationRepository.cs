using LegalLawBot_Csharp.Domain;

namespace LegalLawBot_Csharp.Infrastructure.Repositories;

// Ta klasa służy tylko do testów dopóki nie podepnie sie prawdziwego SQL
public class FakeConsultationRepository : IConsultationRepository
{
    public Task AddAsync(Consultation consultation)
    {
        // Udaje że zapisuje w bazie (wypisze to tylko w konsoli)
        Console.WriteLine($"[FakeRepo] Zapisano konsultację o ID: {consultation.Id}");
        return Task.CompletedTask;
    }

    public Task<Consultation?> GetByIdAsync(Guid id) => Task.FromResult<Consultation?>(null);

    public Task<IEnumerable<Consultation>> GetByUserIdAsync(UserId userId)
        => Task.FromResult(Enumerable.Empty<Consultation>());

    public Task UpdateAsync(Consultation consultation) => Task.CompletedTask;
}