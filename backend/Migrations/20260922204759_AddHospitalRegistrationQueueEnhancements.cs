using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddHospitalRegistrationQueueEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccreditationDocumentName",
                table: "Hospitals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccreditationDocumentUrl",
                table: "Hospitals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Hospitals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPersonEmail",
                table: "Hospitals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPersonName",
                table: "Hospitals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPersonPhone",
                table: "Hospitals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseDocumentName",
                table: "Hospitals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseDocumentUrl",
                table: "Hospitals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationNumber",
                table: "Hospitals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReportName",
                table: "Hospitals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReportUrl",
                table: "Hospitals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResubmittedAt",
                table: "Hospitals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedFields",
                table: "Hospitals",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HospitalApprovalHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdminName = table.Column<string>(type: "text", nullable: true),
                    Comments = table.Column<string>(type: "text", nullable: true),
                    ReportDocumentName = table.Column<string>(type: "text", nullable: true),
                    ReportDocumentUrl = table.Column<string>(type: "text", nullable: true),
                    ChangedFields = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HospitalApprovalHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HospitalApprovalHistories_Hospitals_HospitalId",
                        column: x => x.HospitalId,
                        principalTable: "Hospitals",
                        principalColumn: "HospitalId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HospitalApprovalHistories_Users_AdminId",
                        column: x => x.AdminId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HospitalApprovalHistories_AdminId",
                table: "HospitalApprovalHistories",
                column: "AdminId");

            migrationBuilder.CreateIndex(
                name: "IX_HospitalApprovalHistories_HospitalId",
                table: "HospitalApprovalHistories",
                column: "HospitalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HospitalApprovalHistories");

            migrationBuilder.DropColumn(
                name: "AccreditationDocumentName",
                table: "Hospitals");

            migrationBuilder.DropColumn(
                name: "AccreditationDocumentUrl",
                table: "Hospitals");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Hospitals");

            migrationBuilder.DropColumn(
                name: "ContactPersonEmail",
                table: "Hospitals");

            migrationBuilder.DropColumn(
                name: "ContactPersonName",
                table: "Hospitals");

            migrationBuilder.DropColumn(
                name: "ContactPersonPhone",
                table: "Hospitals");

            migrationBuilder.DropColumn(
                name: "LicenseDocumentName",
                table: "Hospitals");

            migrationBuilder.DropColumn(
                name: "LicenseDocumentUrl",
                table: "Hospitals");

            migrationBuilder.DropColumn(
                name: "RegistrationNumber",
                table: "Hospitals");

            migrationBuilder.DropColumn(
                name: "RejectionReportName",
                table: "Hospitals");

            migrationBuilder.DropColumn(
                name: "RejectionReportUrl",
                table: "Hospitals");

            migrationBuilder.DropColumn(
                name: "ResubmittedAt",
                table: "Hospitals");

            migrationBuilder.DropColumn(
                name: "UpdatedFields",
                table: "Hospitals");
        }
    }
}
