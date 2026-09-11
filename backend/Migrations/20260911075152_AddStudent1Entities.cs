using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddStudent1Entities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Acceptances",
                columns: table => new
                {
                    AcceptanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BloodRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    DonorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Acceptances", x => x.AcceptanceId);
                });

            migrationBuilder.CreateTable(
                name: "BloodRequests",
                columns: table => new
                {
                    BloodRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    BloodGroup = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    UnitsRequired = table.Column<int>(type: "integer", nullable: false),
                    FulfilledUnits = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BloodRequests", x => x.BloodRequestId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Acceptances_AcceptedAt",
                table: "Acceptances",
                column: "AcceptedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Acceptances_BloodRequestId",
                table: "Acceptances",
                column: "BloodRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Acceptances_DonorUserId",
                table: "Acceptances",
                column: "DonorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Acceptances_Status",
                table: "Acceptances",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_BloodRequests_BloodGroup",
                table: "BloodRequests",
                column: "BloodGroup");

            migrationBuilder.CreateIndex(
                name: "IX_BloodRequests_CreatedAt",
                table: "BloodRequests",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_BloodRequests_ExpiryDate",
                table: "BloodRequests",
                column: "ExpiryDate");

            migrationBuilder.CreateIndex(
                name: "IX_BloodRequests_HospitalId",
                table: "BloodRequests",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_BloodRequests_PatientUserId",
                table: "BloodRequests",
                column: "PatientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BloodRequests_Status",
                table: "BloodRequests",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Acceptances");

            migrationBuilder.DropTable(
                name: "BloodRequests");
        }
    }
}
