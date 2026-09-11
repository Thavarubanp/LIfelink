using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddStudent1Enhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConcurrencyToken",
                table: "BloodRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RequestFulfillmentHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BloodRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DonorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FulfilledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestFulfillmentHistories", x => x.Id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_BloodRequests_FulfilledUnits",
                table: "BloodRequests",
                sql: "\"FulfilledUnits\" >= 0 AND \"FulfilledUnits\" <= \"UnitsRequired\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BloodRequests_UnitsRequired",
                table: "BloodRequests",
                sql: "\"UnitsRequired\" >= 1 AND \"UnitsRequired\" <= 10");

            migrationBuilder.CreateIndex(
                name: "IX_RequestFulfillmentHistories_AcceptanceId",
                table: "RequestFulfillmentHistories",
                column: "AcceptanceId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestFulfillmentHistories_BloodRequestId",
                table: "RequestFulfillmentHistories",
                column: "BloodRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestFulfillmentHistories_DonorUserId",
                table: "RequestFulfillmentHistories",
                column: "DonorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestFulfillmentHistories_FulfilledAt",
                table: "RequestFulfillmentHistories",
                column: "FulfilledAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestFulfillmentHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BloodRequests_FulfilledUnits",
                table: "BloodRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BloodRequests_UnitsRequired",
                table: "BloodRequests");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "BloodRequests");
        }
    }
}
