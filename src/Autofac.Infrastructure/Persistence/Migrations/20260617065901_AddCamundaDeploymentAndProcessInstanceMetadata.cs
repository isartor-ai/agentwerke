using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autofac.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCamundaDeploymentAndProcessInstanceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CamundaDeployedAt",
                schema: "autofac",
                table: "workflows",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CamundaDeploymentKey",
                schema: "autofac",
                table: "workflows",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CamundaProcessDefinitionId",
                schema: "autofac",
                table: "workflows",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CamundaProcessDefinitionKey",
                schema: "autofac",
                table: "workflows",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CamundaProcessDefinitionVersion",
                schema: "autofac",
                table: "workflows",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CamundaProcessDefinitionId",
                schema: "autofac",
                table: "workflow_runs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CamundaProcessDefinitionKey",
                schema: "autofac",
                table: "workflow_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CamundaProcessDefinitionVersion",
                schema: "autofac",
                table: "workflow_runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CamundaProcessInstanceKey",
                schema: "autofac",
                table: "workflow_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CamundaDeployedAt",
                schema: "autofac",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "CamundaDeploymentKey",
                schema: "autofac",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "CamundaProcessDefinitionId",
                schema: "autofac",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "CamundaProcessDefinitionKey",
                schema: "autofac",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "CamundaProcessDefinitionVersion",
                schema: "autofac",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "CamundaProcessDefinitionId",
                schema: "autofac",
                table: "workflow_runs");

            migrationBuilder.DropColumn(
                name: "CamundaProcessDefinitionKey",
                schema: "autofac",
                table: "workflow_runs");

            migrationBuilder.DropColumn(
                name: "CamundaProcessDefinitionVersion",
                schema: "autofac",
                table: "workflow_runs");

            migrationBuilder.DropColumn(
                name: "CamundaProcessInstanceKey",
                schema: "autofac",
                table: "workflow_runs");
        }
    }
}
