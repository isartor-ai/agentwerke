using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agentwerke.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowRunStepRuntimeSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use IF NOT EXISTS because a later migration (20260615063250) adds the same
            // column; either one may have already run on an existing database.
            migrationBuilder.Sql(
                @"ALTER TABLE agentwerke.workflow_run_steps ADD COLUMN IF NOT EXISTS ""RuntimeSnapshot"" jsonb;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RuntimeSnapshot",
                schema: "agentwerke",
                table: "workflow_run_steps");
        }
    }
}
