using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class LifecycleScopeUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BloodRequestVerifications_Doctors_DoctorId",
                table: "BloodRequestVerifications");

            migrationBuilder.DropForeignKey(
                name: "FK_DonorPatientMatches_Doctors_DoctorId",
                table: "DonorPatientMatches");

            migrationBuilder.DropForeignKey(
                name: "FK_DonorVerifications_Doctors_DoctorId",
                table: "DonorVerifications");

            migrationBuilder.AlterColumn<Guid>(
                name: "DoctorId",
                table: "DonorVerifications",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "DoctorId",
                table: "DonorPatientMatches",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "TargetUserId",
                table: "Complaints",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "DoctorId",
                table: "BloodRequestVerifications",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "BloodRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_TargetUserId",
                table: "Complaints",
                column: "TargetUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_BloodRequestVerifications_Doctors_DoctorId",
                table: "BloodRequestVerifications",
                column: "DoctorId",
                principalTable: "Doctors",
                principalColumn: "DoctorId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Complaints_Users_TargetUserId",
                table: "Complaints",
                column: "TargetUserId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_DonorPatientMatches_Doctors_DoctorId",
                table: "DonorPatientMatches",
                column: "DoctorId",
                principalTable: "Doctors",
                principalColumn: "DoctorId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_DonorVerifications_Doctors_DoctorId",
                table: "DonorVerifications",
                column: "DoctorId",
                principalTable: "Doctors",
                principalColumn: "DoctorId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BloodRequestVerifications_Doctors_DoctorId",
                table: "BloodRequestVerifications");

            migrationBuilder.DropForeignKey(
                name: "FK_Complaints_Users_TargetUserId",
                table: "Complaints");

            migrationBuilder.DropForeignKey(
                name: "FK_DonorPatientMatches_Doctors_DoctorId",
                table: "DonorPatientMatches");

            migrationBuilder.DropForeignKey(
                name: "FK_DonorVerifications_Doctors_DoctorId",
                table: "DonorVerifications");

            migrationBuilder.DropIndex(
                name: "IX_Complaints_TargetUserId",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "TargetUserId",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "BloodRequests");

            migrationBuilder.AlterColumn<Guid>(
                name: "DoctorId",
                table: "DonorVerifications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "DoctorId",
                table: "DonorPatientMatches",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "DoctorId",
                table: "BloodRequestVerifications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BloodRequestVerifications_Doctors_DoctorId",
                table: "BloodRequestVerifications",
                column: "DoctorId",
                principalTable: "Doctors",
                principalColumn: "DoctorId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DonorPatientMatches_Doctors_DoctorId",
                table: "DonorPatientMatches",
                column: "DoctorId",
                principalTable: "Doctors",
                principalColumn: "DoctorId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DonorVerifications_Doctors_DoctorId",
                table: "DonorVerifications",
                column: "DoctorId",
                principalTable: "Doctors",
                principalColumn: "DoctorId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
