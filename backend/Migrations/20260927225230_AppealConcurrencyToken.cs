using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLink.Migrations
{
    /// <inheritdoc />
    public partial class AppealConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConcurrencyToken",
                table: "Appeals",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "Appeals");
        }
    }
}
