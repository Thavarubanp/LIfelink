using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class GovernanceAppealThreadsSingleAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppealMessages",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppealId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AttachmentUrl = table.Column<string>(type: "text", nullable: true),
                    AttachmentName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppealMessages", x => x.MessageId);
                    table.ForeignKey(
                        name: "FK_AppealMessages_Appeals_AppealId",
                        column: x => x.AppealId,
                        principalTable: "Appeals",
                        principalColumn: "AppealId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppealMessages_Users_AdminId",
                        column: x => x.AdminId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_SingleAdmin",
                table: "UserRoles",
                column: "RoleId",
                unique: true,
                filter: "\"RoleId\" = 4");

            migrationBuilder.CreateIndex(
                name: "IX_AppealMessages_AdminId",
                table: "AppealMessages",
                column: "AdminId");

            migrationBuilder.CreateIndex(
                name: "IX_AppealMessages_AppealId",
                table: "AppealMessages",
                column: "AppealId");

            // Existing appeals become threads: the appellant's reason, then the admin's response (if any)
            migrationBuilder.Sql(@"
                INSERT INTO ""AppealMessages"" (""MessageId"", ""AppealId"", ""AdminId"", ""Message"", ""CreatedAt"")
                SELECT gen_random_uuid(), ""AppealId"", NULL, ""Reason"", ""SubmittedAt"" FROM ""Appeals"";
                INSERT INTO ""AppealMessages"" (""MessageId"", ""AppealId"", ""AdminId"", ""Message"", ""CreatedAt"")
                SELECT gen_random_uuid(), ""AppealId"", ""ReviewedByAdminId"", '[' || ""Status"" || '] ' || ""AdminResponse"",
                       COALESCE(""ReviewedAt"", ""SubmittedAt"" + interval '1 second')
                FROM ""Appeals"" WHERE ""AdminResponse"" IS NOT NULL AND ""AdminResponse"" <> '';");

            // The single Admin holds only the Admin role (it no longer also acts as a donor/patient)
            migrationBuilder.Sql(@"DELETE FROM ""UserRoles"" WHERE ""RoleId"" = 1 AND ""UserId"" IN (SELECT ""UserId"" FROM ""UserRoles"" WHERE ""RoleId"" = 4);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppealMessages");

            migrationBuilder.DropIndex(
                name: "IX_UserRoles_SingleAdmin",
                table: "UserRoles");
        }
    }
}
