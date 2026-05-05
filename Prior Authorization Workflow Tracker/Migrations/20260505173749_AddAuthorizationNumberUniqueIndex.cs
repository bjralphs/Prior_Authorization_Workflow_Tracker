using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prior_Authorization_Workflow_Tracker.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorizationNumberUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_PaRequests_AuthorizationNumber",
                table: "PaRequests",
                column: "AuthorizationNumber",
                unique: true,
                filter: "[AuthorizationNumber] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_PaRequests_AuthorizationNumber",
                table: "PaRequests");
        }
    }
}
