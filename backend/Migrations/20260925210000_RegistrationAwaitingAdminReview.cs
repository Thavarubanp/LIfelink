using LifeLink.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLink.Migrations
{
    /// <summary>
    /// Registration conversation turns: a rejected registration whose latest entry is the hospital's reply is waiting for
    /// the admin, so it moves to the new AwaitingAdminReview status. Data only - the status column is already text.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260925210000_RegistrationAwaitingAdminReview")]
    public partial class RegistrationAwaitingAdminReview : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE ""Hospitals"" h
SET ""ApprovalStatus"" = 'AwaitingAdminReview'
WHERE h.""ApprovalStatus"" = 'Rejected'
  AND (SELECT x.""Status"" FROM ""HospitalApprovalHistories"" x
       WHERE x.""HospitalId"" = h.""HospitalId""
       ORDER BY x.""Timestamp"" DESC LIMIT 1) = 'HospitalReply';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Hospitals\" SET \"ApprovalStatus\" = 'Rejected' WHERE \"ApprovalStatus\" = 'AwaitingAdminReview';");
        }
    }
}
