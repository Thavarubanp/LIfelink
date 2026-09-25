using LifeLink.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLink.Migrations
{
    /// <summary>
    /// Continuous registration conversation: converts values written by the retired resubmission workflow.
    /// Data only - no tables, columns, indexes or constraints change.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260925180000_RegistrationConversationData")]
    public partial class RegistrationConversationData : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hospitals: the "Resubmitted" status no longer exists; those registrations are rejected and awaiting the admin
            migrationBuilder.Sql("UPDATE \"Hospitals\" SET \"ApprovalStatus\" = 'Rejected' WHERE \"ApprovalStatus\" = 'Resubmitted';");

            // Conversation entries: the first entry is "Submitted"; each old resubmission becomes a hospital reply
            migrationBuilder.Sql("UPDATE \"HospitalApprovalHistories\" SET \"Status\" = 'Submitted' WHERE \"Status\" = 'Pending';");
            migrationBuilder.Sql("UPDATE \"HospitalApprovalHistories\" SET \"Status\" = 'HospitalReply' WHERE \"Status\" = 'Resubmitted';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"HospitalApprovalHistories\" SET \"Status\" = 'Resubmitted' WHERE \"Status\" = 'HospitalReply';");
            migrationBuilder.Sql("UPDATE \"HospitalApprovalHistories\" SET \"Status\" = 'Pending' WHERE \"Status\" = 'Submitted';");
            // The old workflow had no comment entries; admin comments read back as rejections
            migrationBuilder.Sql("UPDATE \"HospitalApprovalHistories\" SET \"Status\" = 'Rejected' WHERE \"Status\" = 'AdminComment';");
            // Hospitals converted from "Resubmitted" cannot be told apart from other rejected ones, so they stay "Rejected"
        }
    }
}
