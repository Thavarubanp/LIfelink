using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLink.Migrations
{
    /// <inheritdoc />
    public partial class HospitalPacketsAndDonations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "BloodPacketTrackingNumbers");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByHospitalId",
                table: "BloodPackets",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "HeldForReferenceId",
                table: "BloodPackets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackingNumber",
                table: "BloodPackets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "DonorHospitalId",
                table: "Acceptances",
                type: "uuid",
                nullable: true);

            // Existing packets get tracking numbers in creation order; the sequence then continues after them
            migrationBuilder.Sql(@"
WITH numbered AS (
    SELECT ""PacketId"", row_number() OVER (ORDER BY ""CreatedAt"", ""PacketId"") AS n FROM ""BloodPackets""
)
UPDATE ""BloodPackets"" p
SET ""TrackingNumber"" = 'PKT-' || lpad(numbered.n::text, 8, '0')
FROM numbered
WHERE numbered.""PacketId"" = p.""PacketId"";

SELECT setval('""BloodPacketTrackingNumbers""',
              GREATEST((SELECT COUNT(*) FROM ""BloodPackets""), 1),
              (SELECT COUNT(*) FROM ""BloodPackets"") > 0);");

            // Created-by hospital: the hospital of the packet's first audit row (where it was collected); transfers
            // happened later. Packets without audit rows fall back to their current owner.
            migrationBuilder.Sql(@"
UPDATE ""BloodPackets"" p
SET ""CreatedByHospitalId"" = COALESCE((
    SELECT i.""HospitalId""
    FROM ""InventoryTransactions"" t
    JOIN ""BloodInventories"" i ON i.""InventoryId"" = t.""InventoryId""
    WHERE t.""PacketId"" = p.""PacketId""
    ORDER BY t.""CreatedAt""
    LIMIT 1), p.""HospitalId"");");

            // Safety net: each blood group count equals its Available packets (no-op when already consistent)
            migrationBuilder.Sql(@"
UPDATE ""BloodInventories"" i
SET ""UnitsAvailable"" = c.cnt
FROM (
    SELECT inv.""InventoryId"", (SELECT COUNT(*)::int FROM ""BloodPackets"" p
                                  WHERE p.""HospitalId"" = inv.""HospitalId"" AND p.""BloodGroup"" = inv.""BloodGroup""
                                    AND p.""Status"" = 'Available') AS cnt
    FROM ""BloodInventories"" inv
) c
WHERE c.""InventoryId"" = i.""InventoryId"" AND i.""UnitsAvailable"" <> c.cnt;");

            migrationBuilder.CreateIndex(
                name: "IX_BloodPackets_CreatedByHospitalId",
                table: "BloodPackets",
                column: "CreatedByHospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_BloodPackets_HeldForReferenceId",
                table: "BloodPackets",
                column: "HeldForReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_BloodPackets_TrackingNumber",
                table: "BloodPackets",
                column: "TrackingNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Acceptances_DonorHospitalId",
                table: "Acceptances",
                column: "DonorHospitalId");

            migrationBuilder.AddForeignKey(
                name: "FK_BloodPackets_Hospitals_CreatedByHospitalId",
                table: "BloodPackets",
                column: "CreatedByHospitalId",
                principalTable: "Hospitals",
                principalColumn: "HospitalId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BloodPackets_Hospitals_CreatedByHospitalId",
                table: "BloodPackets");

            migrationBuilder.DropIndex(
                name: "IX_BloodPackets_CreatedByHospitalId",
                table: "BloodPackets");

            migrationBuilder.DropIndex(
                name: "IX_BloodPackets_HeldForReferenceId",
                table: "BloodPackets");

            migrationBuilder.DropIndex(
                name: "IX_BloodPackets_TrackingNumber",
                table: "BloodPackets");

            migrationBuilder.DropIndex(
                name: "IX_Acceptances_DonorHospitalId",
                table: "Acceptances");

            migrationBuilder.DropColumn(
                name: "CreatedByHospitalId",
                table: "BloodPackets");

            migrationBuilder.DropColumn(
                name: "HeldForReferenceId",
                table: "BloodPackets");

            migrationBuilder.DropColumn(
                name: "TrackingNumber",
                table: "BloodPackets");

            migrationBuilder.DropColumn(
                name: "DonorHospitalId",
                table: "Acceptances");

            migrationBuilder.DropSequence(
                name: "BloodPacketTrackingNumbers");
        }
    }
}
