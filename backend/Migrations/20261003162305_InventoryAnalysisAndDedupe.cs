using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLink.Migrations
{
    /// <inheritdoc />
    public partial class InventoryAnalysisAndDedupe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DedupeKey",
                table: "Notifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventoryAnalysisRuns",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Trigger = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TriggeredByHospitalId = table.Column<Guid>(type: "uuid", nullable: true),
                    TriggeredByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LowStockAlerts = table.Column<int>(type: "integer", nullable: false),
                    ExpiringAlerts = table.Column<int>(type: "integer", nullable: false),
                    SkippedDuplicates = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryAnalysisRuns", x => x.RunId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_HospitalId_DedupeKey",
                table: "Notifications",
                columns: new[] { "HospitalId", "DedupeKey" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAnalysisRuns_StartedAt",
                table: "InventoryAnalysisRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAnalysisRuns_Trigger_StartedAt",
                table: "InventoryAnalysisRuns",
                columns: new[] { "Trigger", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryAnalysisRuns");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_HospitalId_DedupeKey",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "DedupeKey",
                table: "Notifications");
        }
    }
}
