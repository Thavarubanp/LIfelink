using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AgenticExpansionPacketsAndReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BloodGroup",
                table: "Users",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastDonationDate",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PacketId",
                table: "InventoryTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PerformedByUserId",
                table: "InventoryTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReferenceId",
                table: "InventoryTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "HospitalTransferRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransferType",
                table: "HospitalTransferRequests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Request");

            migrationBuilder.AddColumn<int>(
                name: "ExpiryAlertDays",
                table: "Hospitals",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "PacketShelfLifeDays",
                table: "Hospitals",
                type: "integer",
                nullable: false,
                defaultValue: 35);

            migrationBuilder.AddColumn<Guid>(
                name: "DecidedByDoctorId",
                table: "DonorVerifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportJson",
                table: "DonorVerifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReportVersion",
                table: "DonorVerifications",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "ReservedUnits",
                table: "BloodRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ConcurrencyToken",
                table: "BloodInventories",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BloodPackets",
                columns: table => new
                {
                    PacketId = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    BloodGroup = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    VolumeMl = table.Column<int>(type: "integer", nullable: false, defaultValue: 440),
                    CollectionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConcurrencyToken = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BloodPackets", x => x.PacketId);
                    table.ForeignKey(
                        name: "FK_BloodPackets_Hospitals_HospitalId",
                        column: x => x.HospitalId,
                        principalTable: "Hospitals",
                        principalColumn: "HospitalId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_PacketId",
                table: "InventoryTransactions",
                column: "PacketId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_ReferenceId",
                table: "InventoryTransactions",
                column: "ReferenceId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Hospitals_ExpiryAlertDays",
                table: "Hospitals",
                sql: "\"ExpiryAlertDays\" >= 1 AND \"ExpiryAlertDays\" <= 20");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Hospitals_PacketShelfLifeDays",
                table: "Hospitals",
                sql: "\"PacketShelfLifeDays\" >= 21 AND \"PacketShelfLifeDays\" <= 35");

            migrationBuilder.CreateIndex(
                name: "IX_DonorVerifications_AcceptanceId_ReportVersion",
                table: "DonorVerifications",
                columns: new[] { "AcceptanceId", "ReportVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DonorVerifications_DecidedByDoctorId",
                table: "DonorVerifications",
                column: "DecidedByDoctorId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BloodRequests_ReservedUnits",
                table: "BloodRequests",
                sql: "\"ReservedUnits\" >= 0 AND \"FulfilledUnits\" + \"ReservedUnits\" <= \"UnitsRequired\"");

            migrationBuilder.CreateIndex(
                name: "IX_BloodPackets_ExpiryDate",
                table: "BloodPackets",
                column: "ExpiryDate");

            migrationBuilder.CreateIndex(
                name: "IX_BloodPackets_HospitalId_BloodGroup_Status",
                table: "BloodPackets",
                columns: new[] { "HospitalId", "BloodGroup", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BloodPackets_SourceReferenceId",
                table: "BloodPackets",
                column: "SourceReferenceId");

            migrationBuilder.AddForeignKey(
                name: "FK_DonorVerifications_Doctors_DecidedByDoctorId",
                table: "DonorVerifications",
                column: "DecidedByDoctorId",
                principalTable: "Doctors",
                principalColumn: "DoctorId",
                onDelete: ReferentialAction.SetNull);

            // Backfill: donors a doctor already approved (Verified) hold a reserved slot on open requests
            migrationBuilder.Sql(@"
UPDATE ""BloodRequests"" AS r
SET ""ReservedUnits"" = LEAST(v.cnt, GREATEST(r.""UnitsRequired"" - r.""FulfilledUnits"", 0))
FROM (SELECT ""BloodRequestId"", COUNT(*)::int AS cnt FROM ""Acceptances"" WHERE ""Status"" = 'Verified' GROUP BY ""BloodRequestId"") AS v
WHERE v.""BloodRequestId"" = r.""BloodRequestId"" AND r.""Status"" = 'Approved';");

            // Backfill: stock counted before packet tracking becomes audited Legacy packets (one per unit)
            migrationBuilder.Sql(@"
INSERT INTO ""BloodPackets"" (""PacketId"", ""HospitalId"", ""BloodGroup"", ""VolumeMl"", ""CollectionDate"", ""ExpiryDate"", ""Status"", ""Source"", ""SourceReferenceId"", ""ConcurrencyToken"", ""CreatedAt"", ""UpdatedAt"")
SELECT gen_random_uuid(), i.""HospitalId"", i.""BloodGroup"", 440, now(), now() + make_interval(days => h.""PacketShelfLifeDays""), 'Available', 'Legacy', NULL, 0, now(), now()
FROM ""BloodInventories"" i
JOIN ""Hospitals"" h ON h.""HospitalId"" = i.""HospitalId""
CROSS JOIN LATERAL generate_series(1, i.""UnitsAvailable"") AS g(n)
WHERE i.""UnitsAvailable"" > 0;

INSERT INTO ""InventoryTransactions"" (""TransactionId"", ""InventoryId"", ""TransactionType"", ""Units"", ""Notes"", ""CreatedAt"", ""PacketId"")
SELECT gen_random_uuid(), i.""InventoryId"", 'LEGACY_MIGRATED', 1, 'Stock recorded before packet tracking; converted to a Legacy packet by migration', now(), p.""PacketId""
FROM ""BloodPackets"" p
JOIN ""BloodInventories"" i ON i.""HospitalId"" = p.""HospitalId"" AND i.""BloodGroup"" = p.""BloodGroup""
WHERE p.""Source"" = 'Legacy';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DonorVerifications_Doctors_DecidedByDoctorId",
                table: "DonorVerifications");

            migrationBuilder.DropTable(
                name: "BloodPackets");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_PacketId",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_ReferenceId",
                table: "InventoryTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Hospitals_ExpiryAlertDays",
                table: "Hospitals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Hospitals_PacketShelfLifeDays",
                table: "Hospitals");

            migrationBuilder.DropIndex(
                name: "IX_DonorVerifications_AcceptanceId_ReportVersion",
                table: "DonorVerifications");

            migrationBuilder.DropIndex(
                name: "IX_DonorVerifications_DecidedByDoctorId",
                table: "DonorVerifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BloodRequests_ReservedUnits",
                table: "BloodRequests");

            migrationBuilder.DropColumn(
                name: "BloodGroup",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastDonationDate",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PacketId",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "PerformedByUserId",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "ReferenceId",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "HospitalTransferRequests");

            migrationBuilder.DropColumn(
                name: "TransferType",
                table: "HospitalTransferRequests");

            migrationBuilder.DropColumn(
                name: "ExpiryAlertDays",
                table: "Hospitals");

            migrationBuilder.DropColumn(
                name: "PacketShelfLifeDays",
                table: "Hospitals");

            migrationBuilder.DropColumn(
                name: "DecidedByDoctorId",
                table: "DonorVerifications");

            migrationBuilder.DropColumn(
                name: "ReportJson",
                table: "DonorVerifications");

            migrationBuilder.DropColumn(
                name: "ReportVersion",
                table: "DonorVerifications");

            migrationBuilder.DropColumn(
                name: "ReservedUnits",
                table: "BloodRequests");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "BloodInventories");
        }
    }
}
