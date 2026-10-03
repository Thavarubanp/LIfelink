using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLink.Migrations
{
    /// <inheritdoc />
    public partial class AdminSuspensionOfRequestsAndTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AdminSuspendedAt",
                table: "HospitalTransferRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AdminSuspendedByUserId",
                table: "HospitalTransferRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdminSuspensionReason",
                table: "HospitalTransferRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AdminSuspendedAt",
                table: "BloodRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AdminSuspendedByUserId",
                table: "BloodRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdminSuspensionReason",
                table: "BloodRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminSuspendedAt",
                table: "HospitalTransferRequests");

            migrationBuilder.DropColumn(
                name: "AdminSuspendedByUserId",
                table: "HospitalTransferRequests");

            migrationBuilder.DropColumn(
                name: "AdminSuspensionReason",
                table: "HospitalTransferRequests");

            migrationBuilder.DropColumn(
                name: "AdminSuspendedAt",
                table: "BloodRequests");

            migrationBuilder.DropColumn(
                name: "AdminSuspendedByUserId",
                table: "BloodRequests");

            migrationBuilder.DropColumn(
                name: "AdminSuspensionReason",
                table: "BloodRequests");
        }
    }
}
