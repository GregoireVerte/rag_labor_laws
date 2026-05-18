using LegalLawBot_Csharp.Domain;
using Microsoft.EntityFrameworkCore;

namespace LegalLawBot_Csharp.Infrastructure.Persistence;

public class EfUserRepository : IUserRepository
{
    private readonly LegalLawBotDbContext _context;

    // Wstrzykuje DbContext przez konstruktor
    public EfUserRepository(LegalLawBotDbContext context)
    {
        _context = context;
    }

    // Pobieranie użytkownika z bazy Supabase
    public async Task<User?> GetByIdAsync(UserId id)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Id == id);
    }

    // Dodawanie nowego użytkownika do bazy
    public async Task AddAsync(User user)
    {
        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();
    }
}