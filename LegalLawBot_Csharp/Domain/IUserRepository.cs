using LegalLawBot_Csharp.Domain;

namespace LegalLawBot_Csharp.Domain;

public interface IUserRepository
{
    // Pobieranie użytkownika po jego bezpiecznym ID
    Task<User?> GetByIdAsync(UserId id);

    // Dodawanie nowego użytkownika do bazy danych
    Task AddAsync(User user);
}