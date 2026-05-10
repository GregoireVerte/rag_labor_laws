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
        // sprawdzenie czy obiekt jest już śledzony, jeśli nie jest - zostaje dołączony
        var entry = _context.Entry(consultation);
        if (entry.State == EntityState.Detached)
        {
            _context.Consultations.Attach(consultation);
        }

        // zapis zmian w bazie w całym agregacie (w tym w nowo dodanych wiadomościach)
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