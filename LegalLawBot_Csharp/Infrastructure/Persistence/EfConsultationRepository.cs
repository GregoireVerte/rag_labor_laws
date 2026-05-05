using LegalLawBot_Csharp.Domain;
using Microsoft.EntityFrameworkCore;

namespace LegalLawBot_Csharp.Infrastructure.Persistence;

public class EfConsultationRepository : IConsultationRepository
{
    private readonly LegalLawBotDbContext _context;

    public EfConsultationRepository(LegalLawBotDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(Consultation consultation)
    {
        await _context.Consultations.AddAsync(consultation);
        await _context.SaveChangesAsync();
    }

    public async Task<Consultation?> GetByIdAsync(Guid id)
    {
        return await _context.Consultations
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task UpdateAsync(Consultation consultation)
    {
        // Informuje EF Core że ten obiekt mógł zostać zmieniony
        _context.Consultations.Update(consultation);
        // Zapisuje zmiany w bazie
        await _context.SaveChangesAsync();
    }

    public async Task<IEnumerable<Consultation>> GetByUserIdAsync(UserId userId)
    {
        // Pobiera wszystkie konsultacje danego użytkownika
        return await _context.Consultations
            .Where(c => c.CreatedBy == userId)
            .ToListAsync();
    }
}