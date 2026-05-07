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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<ArticleId>();
        modelBuilder.Ignore<UserId>();

        // konfiguracja szczegółów tabel

        modelBuilder.Entity<Consultation>(entity =>
        {
            // Id konsultacji to klucz główny
            entity.HasKey(c => c.Id);

            entity.Ignore(c => c.State);

            // Konwerter dla UserId
            entity.Property(c => c.CreatedBy)
                .HasConversion(
                    v => v.Value,            // Z UserId na Guid (do bazy)
                    v => UserId.Create(v));  // Z Guid na UserId (z bazy przez fabrykę)

            // Relacja Jeden-do-Wielu (Consultation -> Messages)
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

            entity.Property(m => m.Role).HasConversion<string>(); // Zapisuje enum jako tekst (User/Assistant)
            entity.Property(m => m.Content).IsRequired();

            // Przenosi konwerter źródeł tutaj (z Consultation do Message)
            entity.Property(m => m.Sources)
                .HasConversion(
                    v => string.Join(',', v.Select(a => a.Value)),
                    v => v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                          .Select(ArticleId.Create)
                          .ToList());
        });

        base.OnModelCreating(modelBuilder);
    }
}