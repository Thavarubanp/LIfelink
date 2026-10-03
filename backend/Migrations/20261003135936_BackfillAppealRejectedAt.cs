using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLink.Migrations
{
    /// <summary>
    /// Data only: appeals rejected before Appeals.RejectedAt existed get it from their first "[REJECTED]" admin
    /// decision message (written by every rejection), so the reject-once rule can rely on RejectedAt alone.
    /// </summary>
    public partial class BackfillAppealRejectedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Appeals" AS a
                SET "RejectedAt" = m."FirstRejection"
                FROM (
                    SELECT "AppealId", MIN("CreatedAt") AS "FirstRejection"
                    FROM "AppealMessages"
                    WHERE "AdminId" IS NOT NULL AND "Message" LIKE '[REJECTED]%'
                    GROUP BY "AppealId"
                ) AS m
                WHERE a."AppealId" = m."AppealId" AND a."RejectedAt" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The backfilled values cannot be told apart from later rejections, so they are kept
        }
    }
}
