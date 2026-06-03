using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalLawBot_Csharp.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveConsultationToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Zostawia TYLKO to, bo tylko tego jednego pola fizycznie brakuje w Supabase!
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveConsultationId",
                table: "Users",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // W razie cofania migracji usuwa tylko tę jedną kolumnę
            migrationBuilder.DropColumn(
                name: "ActiveConsultationId",
                table: "Users");
        }
    }
}