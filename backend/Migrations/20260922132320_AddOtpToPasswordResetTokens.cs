using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddOtpToPasswordResetTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsVerified",
                table: "PasswordResetTokens",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSentAt",
                table: "PasswordResetTokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Otp",
                table: "PasswordResetTokens",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResetSessionToken",
                table: "PasswordResetTokens",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsVerified",
                table: "PasswordResetTokens");

            migrationBuilder.DropColumn(
                name: "LastSentAt",
                table: "PasswordResetTokens");

            migrationBuilder.DropColumn(
                name: "Otp",
                table: "PasswordResetTokens");

            migrationBuilder.DropColumn(
                name: "ResetSessionToken",
                table: "PasswordResetTokens");
        }
    }
}
