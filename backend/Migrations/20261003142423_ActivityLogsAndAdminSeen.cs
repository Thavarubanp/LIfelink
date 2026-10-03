using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLink.Migrations
{
    /// <inheritdoc />
    public partial class ActivityLogsAndAdminSeen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorRole = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ActorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubjectUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdminSeenMarkers",
                columns: table => new
                {
                    AdminUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Area = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminSeenMarkers", x => new { x.AdminUserId, x.Area });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_ActorUserId_OccurredAt",
                table: "ActivityLogs",
                columns: new[] { "ActorUserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_HospitalId_OccurredAt",
                table: "ActivityLogs",
                columns: new[] { "HospitalId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_SubjectUserId_OccurredAt",
                table: "ActivityLogs",
                columns: new[] { "SubjectUserId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityLogs");

            migrationBuilder.DropTable(
                name: "AdminSeenMarkers");
        }
    }
}
