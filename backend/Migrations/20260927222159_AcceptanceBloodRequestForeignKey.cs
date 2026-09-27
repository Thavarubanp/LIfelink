using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeLink.Migrations
{
    /// <inheritdoc />
    public partial class AcceptanceBloodRequestForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_Acceptances_BloodRequests_BloodRequestId",
                table: "Acceptances",
                column: "BloodRequestId",
                principalTable: "BloodRequests",
                principalColumn: "BloodRequestId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Acceptances_BloodRequests_BloodRequestId",
                table: "Acceptances");
        }
    }
}
