using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agentwerke.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStepErrorColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Error",
                schema: "autofac",
                table: "workflow_run_steps",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Error",
                schema: "autofac",
                table: "workflow_run_steps");
        }
    }
}
