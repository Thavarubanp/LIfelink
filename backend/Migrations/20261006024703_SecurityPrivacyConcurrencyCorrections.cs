using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLink.Migrations
{
    /// <inheritdoc />
    public partial class SecurityPrivacyConcurrencyCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Acceptances_DonorUserId",
                table: "Acceptances");

            migrationBuilder.AddColumn<bool>(
                name: "IsAddressPublic",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsEmailPublic",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPhonePublic",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "FailedAttempts",
                table: "PasswordResetTokens",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Acceptances_OneActiveDonorProcess",
                table: "Acceptances",
                column: "DonorUserId",
                unique: true,
                filter: "\"DonorHospitalId\" IS NULL AND \"Status\" IN ('Accepted', 'ScreeningPending', 'ScreeningCompleted', 'Verified')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Acceptances_OneActiveDonorProcess",
                table: "Acceptances");

            migrationBuilder.DropColumn(
                name: "IsAddressPublic",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsEmailPublic",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsPhonePublic",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FailedAttempts",
                table: "PasswordResetTokens");

            migrationBuilder.CreateIndex(
                name: "IX_Acceptances_DonorUserId",
                table: "Acceptances",
                column: "DonorUserId");
        }
    }
}
