using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autofac.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimeSnapshotToWorkflowRunStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RuntimeSnapshot",
                schema: "autofac",
                table: "workflow_run_steps",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RuntimeSnapshot",
                schema: "autofac",
                table: "workflow_run_steps");
        }
    }
}
