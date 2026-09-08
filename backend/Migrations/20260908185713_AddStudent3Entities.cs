using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddStudent3Entities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Hospitals",
                columns: table => new
                {
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LicenseNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    ContactNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hospitals", x => x.HospitalId);
                });

            migrationBuilder.CreateTable(
                name: "BloodInventories",
                columns: table => new
                {
                    InventoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    BloodGroup = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    UnitsAvailable = table.Column<int>(type: "integer", nullable: false),
                    MinimumThreshold = table.Column<int>(type: "integer", nullable: false),
                    MaximumCapacity = table.Column<int>(type: "integer", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BloodInventories", x => x.InventoryId);
                    table.ForeignKey(
                        name: "FK_BloodInventories_Hospitals_HospitalId",
                        column: x => x.HospitalId,
                        principalTable: "Hospitals",
                        principalColumn: "HospitalId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmergencyRequests",
                columns: table => new
                {
                    EmergencyRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    BloodGroup = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    UnitsRequired = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencyRequests", x => x.EmergencyRequestId);
                    table.ForeignKey(
                        name: "FK_EmergencyRequests_Hospitals_HospitalId",
                        column: x => x.HospitalId,
                        principalTable: "Hospitals",
                        principalColumn: "HospitalId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HospitalTransferRequests",
                columns: table => new
                {
                    TransferRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderHospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiverHospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    BloodGroup = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    UnitsRequested = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HospitalTransferRequests", x => x.TransferRequestId);
                    table.ForeignKey(
                        name: "FK_HospitalTransferRequests_Hospitals_ReceiverHospitalId",
                        column: x => x.ReceiverHospitalId,
                        principalTable: "Hospitals",
                        principalColumn: "HospitalId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HospitalTransferRequests_Hospitals_SenderHospitalId",
                        column: x => x.SenderHospitalId,
                        principalTable: "Hospitals",
                        principalColumn: "HospitalId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryTransactions",
                columns: table => new
                {
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Units = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryTransactions", x => x.TransactionId);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_BloodInventories_InventoryId",
                        column: x => x.InventoryId,
                        principalTable: "BloodInventories",
                        principalColumn: "InventoryId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BloodInventories_BloodGroup",
                table: "BloodInventories",
                column: "BloodGroup");

            migrationBuilder.CreateIndex(
                name: "IX_BloodInventories_HospitalId",
                table: "BloodInventories",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_BloodInventories_HospitalId_BloodGroup",
                table: "BloodInventories",
                columns: new[] { "HospitalId", "BloodGroup" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyRequests_BloodGroup",
                table: "EmergencyRequests",
                column: "BloodGroup");

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyRequests_HospitalId",
                table: "EmergencyRequests",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyRequests_Priority",
                table: "EmergencyRequests",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyRequests_Status",
                table: "EmergencyRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_HospitalTransferRequests_BloodGroup",
                table: "HospitalTransferRequests",
                column: "BloodGroup");

            migrationBuilder.CreateIndex(
                name: "IX_HospitalTransferRequests_ReceiverHospitalId",
                table: "HospitalTransferRequests",
                column: "ReceiverHospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_HospitalTransferRequests_SenderHospitalId",
                table: "HospitalTransferRequests",
                column: "SenderHospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_HospitalTransferRequests_Status",
                table: "HospitalTransferRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_InventoryId",
                table: "InventoryTransactions",
                column: "InventoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmergencyRequests");

            migrationBuilder.DropTable(
                name: "HospitalTransferRequests");

            migrationBuilder.DropTable(
                name: "InventoryTransactions");

            migrationBuilder.DropTable(
                name: "BloodInventories");

            migrationBuilder.DropTable(
                name: "Hospitals");
        }
    }
}
