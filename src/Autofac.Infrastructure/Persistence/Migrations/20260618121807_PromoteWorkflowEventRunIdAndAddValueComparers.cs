using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autofac.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PromoteWorkflowEventRunIdAndAddValueComparers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_workflow_events_workflow_runs_RunId",
                schema: "autofac",
                table: "workflow_events");

            migrationBuilder.AlterColumn<string>(
                name: "RunId",
                schema: "autofac",
                table: "workflow_events",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_events_workflow_runs_RunId",
                schema: "autofac",
                table: "workflow_events",
                column: "RunId",
                principalSchema: "autofac",
                principalTable: "workflow_runs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_workflow_events_workflow_runs_RunId",
                schema: "autofac",
                table: "workflow_events");

            migrationBuilder.AlterColumn<string>(
                name: "RunId",
                schema: "autofac",
                table: "workflow_events",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_events_workflow_runs_RunId",
                schema: "autofac",
                table: "workflow_events",
                column: "RunId",
                principalSchema: "autofac",
                principalTable: "workflow_runs",
                principalColumn: "Id");
        }
    }
}
