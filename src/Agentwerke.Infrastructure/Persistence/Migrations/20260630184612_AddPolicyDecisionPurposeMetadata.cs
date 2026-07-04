using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agentwerke.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPolicyDecisionPurposeMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PolicyDecision_PurposeConfidence",
                schema: "agentwerke",
                table: "workflow_run_steps",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyDecision_PurposeRationale",
                schema: "agentwerke",
                table: "workflow_run_steps",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PolicyDecision_PurposeConfidence",
                schema: "agentwerke",
                table: "workflow_run_steps");

            migrationBuilder.DropColumn(
                name: "PolicyDecision_PurposeRationale",
                schema: "agentwerke",
                table: "workflow_run_steps");
        }
    }
}
