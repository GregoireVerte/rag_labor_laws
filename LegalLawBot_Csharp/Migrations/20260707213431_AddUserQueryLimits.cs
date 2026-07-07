using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalLawBot_Csharp.Migrations
{
    /// <inheritdoc />
    public partial class AddUserQueryLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DailyQueryCount",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxDailyLimit",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DailyQueryCount",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MaxDailyLimit",
                table: "Users");
        }
    }
}
