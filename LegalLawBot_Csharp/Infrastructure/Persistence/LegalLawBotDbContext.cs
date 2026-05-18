using Microsoft.EntityFrameworkCore;
using LegalLawBot_Csharp.Domain;

namespace LegalLawBot_Csharp.Infrastructure.Persistence;

// DbContext to klasa-matka w Entity Framework Core // Reprezentuje sesję z bazą danych //
public class LegalLawBotDbContext : DbContext
{
    public LegalLawBotDbContext(DbContextOptions<LegalLawBotDbContext> options)
        : base(options)
    {
    }

    // Definicje zbiorów danych (tabele) // każda właściwość DbSet odpowiada tabeli w bazie danych //
    public DbSet<Consultation> Consultations { get; set; }
    public DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<ArticleId>();
        modelBuilder.Ignore<UserId>();

        // konfiguracja porównywania dla listy artykułów (Sources)
        var articleIdListComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlyList<ArticleId>>(
        (c1, c2) => c1!.SequenceEqual(c2!),
        c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
        c => c.ToList());

        // konfiguracja porównywania dla UserId
        var userIdComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<UserId>(
            (l, r) => l!.Value == r!.Value,
            v => v.Value.GetHashCode(),
            v => v);

        // konfiguracja szczegółów tabel

        modelBuilder.Entity<Consultation>(entity =>
        {
            // Id konsultacji to klucz główny
            entity.HasKey(c => c.Id);

            entity.Ignore(c => c.State);

            entity.Ignore(c => c.LastQuestion);
            entity.Ignore(c => c.LastResponse);
            entity.Ignore(c => c.LastSources);

            entity.Property(c => c.CreatedAt).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);

            // Konwerter dla UserId
            entity.Property(c => c.CreatedBy)
                .HasConversion(
                    v => v.Value,            // Z UserId na Guid (do bazy)
                    v => UserId.Create(v))  // Z Guid na UserId (z bazy przez fabrykę)
                .Metadata.SetValueComparer(userIdComparer);

            // Relacja Jeden-do-Wielu (Consultation -> Messages) i dostęp do pola prywatnego
            // Mówi EF Core że pole prywatne _messages ma być traktowane jako kolekcja
            entity.HasMany(c => c.Messages)
                .WithOne() // Wiadomość należy do jednej konsultacji
                .HasForeignKey("ConsultationId") // Klucz obcy w tabeli Messages
                .OnDelete(DeleteBehavior.Cascade); // Usunięcie sesji usuwa jej wiadomości

            // Dostęp do pola prywatnego dla EF Core
            var navigation = entity.Metadata.FindNavigation(nameof(Consultation.Messages));
            navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        // Konfiguracja tabeli wiadomości
        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("Messages");
            entity.HasKey(m => m.Id);

            entity.Property(m => m.Id).ValueGeneratedNever();

            entity.Property(m => m.Role).HasConversion<string>(); // Zapisuje enum jako tekst (User/Assistant)
            entity.Property(m => m.Content).IsRequired();

            // Przenosi konwerter źródeł tutaj (z Consultation do Message)
            entity.Property(m => m.Sources)
                .HasConversion(
                    v => string.Join(',', v.Select(a => a.Value)),
                    v => v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                          .Select(ArticleId.Create)
                          .ToList())
                .Metadata.SetValueComparer(articleIdListComparer);
        });

        // Konfiguracja tabeli użytkowników
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(u => u.Id);

            // 1. Konwerter dla UserId (używa tego samego userIdComparer który już jest w pliku)
            entity.Property(u => u.Id)
                .HasConversion(
                    v => v.Value,            // Z UserId na Guid do bazy
                    v => UserId.Create(v))  // Z Guid na UserId z bazy
                .Metadata.SetValueComparer(userIdComparer);

            // 2. Konwerter dla EmailAddress
            entity.Property(u => u.Email)
                .HasConversion(
                    v => v.Value,
                    v => EmailAddress.Create(v));

            // 3. Konwerter dla UserStatus
            entity.Property(u => u.Status)
                .HasConversion(
                    v => v.Value,
                    v => v == "Zablokowany" ? UserStatus.Zablokowany : UserStatus.Aktywny);

            // 4. Konwerter dla UserRole
            entity.Property(u => u.Role)
                .HasConversion(
                    v => v.Name,
                    v => v == "Administrator" ? UserRole.Administrator : UserRole.Standard);
        });

        base.OnModelCreating(modelBuilder);
    }
}