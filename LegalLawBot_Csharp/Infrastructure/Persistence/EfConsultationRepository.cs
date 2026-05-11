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
        // dodane .Include(c => c.Messages) aby załadować historię sesji
        return await _context.Consultations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task UpdateAsync(Consultation consultation)
    {
        var entry = _context.Entry(consultation);

        // jeśli EF Core chce aktualizować rodzica (Consultation) mówi mu że nie trzeba
        // to zapobiegnie błędowi 500 jeśli Postgres uzna że update rodzica był zbędny
        if (entry.State == EntityState.Modified)
        {
            entry.State = EntityState.Unchanged;
        }

        await _context.SaveChangesAsync();
    }

    public async Task<IEnumerable<Consultation>> GetByUserIdAsync(UserId userId)
    {
        // Pobiera wszystkie konsultacje danego użytkownika
        return await _context.Consultations
            .Where(c => c.CreatedBy == userId)
            .Include(c => c.Messages) // dołożone by widzieć historię
            .ToListAsync();
    }
}