using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLink.Migrations
{
    /// <inheritdoc />
    public partial class RaceConditionSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConcurrencyToken",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ConcurrencyToken",
                table: "HospitalTransferRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ConcurrencyToken",
                table: "Hospitals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ConcurrencyToken",
                table: "EmergencyRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ConcurrencyToken",
                table: "Complaints",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ConcurrencyToken",
                table: "Acceptances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "IdempotencyKeys",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Endpoint = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyKeys", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BloodRequests_OneActivePerCreatorHospitalGroup",
                table: "BloodRequests",
                columns: new[] { "PatientUserId", "HospitalId", "BloodGroup" },
                unique: true,
                filter: "\"Status\" IN ('Pending', 'Verified', 'Approved')");

            migrationBuilder.CreateIndex(
                name: "IX_Appeals_OneOpenPerHospital",
                table: "Appeals",
                column: "HospitalId",
                unique: true,
                filter: "\"HospitalId\" IS NOT NULL AND \"Status\" IN ('PENDING', 'REJECTED')");

            migrationBuilder.CreateIndex(
                name: "IX_Appeals_OneOpenPerUser",
                table: "Appeals",
                column: "UserId",
                unique: true,
                filter: "\"HospitalId\" IS NULL AND \"Status\" IN ('PENDING', 'REJECTED')");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyKeys_CreatedAt",
                table: "IdempotencyKeys",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyKeys");

            migrationBuilder.DropIndex(
                name: "IX_BloodRequests_OneActivePerCreatorHospitalGroup",
                table: "BloodRequests");

            migrationBuilder.DropIndex(
                name: "IX_Appeals_OneOpenPerHospital",
                table: "Appeals");

            migrationBuilder.DropIndex(
                name: "IX_Appeals_OneOpenPerUser",
                table: "Appeals");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "HospitalTransferRequests");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "Hospitals");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "EmergencyRequests");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "Acceptances");
        }
    }
}
