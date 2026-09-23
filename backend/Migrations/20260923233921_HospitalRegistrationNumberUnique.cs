using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class HospitalRegistrationNumberUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Remove the two auto-generated "Hospital 00000000" placeholders (no registration number, no login,
            //    doctors, inventory, requests or transfers). Their approval history and notifications cascade.
            migrationBuilder.Sql(@"DELETE FROM ""Hospitals"" WHERE ""HospitalId"" IN
                ('3e152dfc-08ff-4aae-a773-73993350fb83', '7d1556eb-734a-4989-8a76-d20855c7ce82');");

            // 2. Registration numbers are stored trimmed + upper-cased (only rows that differ are updated)
            migrationBuilder.Sql(@"UPDATE ""Hospitals"" SET ""RegistrationNumber"" = NULLIF(UPPER(TRIM(""RegistrationNumber"")), '')
                WHERE ""RegistrationNumber"" IS DISTINCT FROM NULLIF(UPPER(TRIM(""RegistrationNumber"")), '');");

            // 3. Abort if any NULL/empty or duplicate registration numbers remain
            migrationBuilder.Sql(@"DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM ""Hospitals"" WHERE ""RegistrationNumber"" IS NULL) THEN
                        RAISE EXCEPTION 'Hospitals without a registration number remain; clean them up before adding the unique index.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM ""Hospitals"" GROUP BY ""RegistrationNumber"" HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'Duplicate hospital registration numbers remain; clean them up before adding the unique index.';
                    END IF;
                END $$;");

            migrationBuilder.CreateIndex(
                name: "IX_Hospitals_RegistrationNumber",
                table: "Hospitals",
                column: "RegistrationNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The deleted placeholder hospitals are not restored.
            migrationBuilder.DropIndex(
                name: "IX_Hospitals_RegistrationNumber",
                table: "Hospitals");
        }
    }
}
