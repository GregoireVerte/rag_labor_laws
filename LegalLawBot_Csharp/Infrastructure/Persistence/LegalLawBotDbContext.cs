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
        // konfigururacja szczegółów tabel

        modelBuilder.Entity<Consultation>(entity =>
        {
            // Id konsultacji to klucz główny
            entity.HasKey(c => c.Id);

            entity.Ignore(c => c.State);

            // DLA USERID
            entity.Property(c => c.CreatedBy)
                .HasConversion(
                    v => v.Value,            // Z UserId na Guid (do bazy)
                    v => UserId.Create(v));  // Z Guid na UserId (z bazy przez fabrykę)

            // konfiguruje że tekst odpowiedzi może być bardzo długi
            entity.Property(c => c.Response).IsRequired();

            entity.Property(c => c.Sources)
                .HasConversion(
                    v => string.Join(',', v.Select(a => a.Value)),
                    v => v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                          .Select(ArticleId.Create)
                          .ToList());
        });

        base.OnModelCreating(modelBuilder);
    }
}