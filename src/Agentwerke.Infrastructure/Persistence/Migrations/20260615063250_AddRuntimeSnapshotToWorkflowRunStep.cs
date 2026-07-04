using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agentwerke.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimeSnapshotToWorkflowRunStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IF NOT EXISTS guard: 20260614162015 adds the same column; either may have
            // already run on an existing database.
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
