using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autofac.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalRequestArtifactName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArtifactName",
                schema: "autofac",
                table: "approval_requests",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArtifactName",
                schema: "autofac",
                table: "approval_requests");
        }
    }
}
