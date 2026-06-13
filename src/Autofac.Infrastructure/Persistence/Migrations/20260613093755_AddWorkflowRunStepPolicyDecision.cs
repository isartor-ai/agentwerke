using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autofac.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowRunStepPolicyDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "PolicyDecision_Constraints",
                schema: "autofac",
                table: "workflow_run_steps",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyDecision_DecidedAt",
                schema: "autofac",
                table: "workflow_run_steps",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyDecision_Kind",
                schema: "autofac",
                table: "workflow_run_steps",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyDecision_PolicyId",
                schema: "autofac",
                table: "workflow_run_steps",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyDecision_PolicyName",
                schema: "autofac",
                table: "workflow_run_steps",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyDecision_Rationale",
                schema: "autofac",
                table: "workflow_run_steps",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "PolicyDecision_RiskFactors",
                schema: "autofac",
                table: "workflow_run_steps",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyDecision_RiskLevel",
                schema: "autofac",
                table: "workflow_run_steps",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PolicyDecision_RiskScore",
                schema: "autofac",
                table: "workflow_run_steps",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PolicyDecision_Constraints",
                schema: "autofac",
                table: "workflow_run_steps");

            migrationBuilder.DropColumn(
                name: "PolicyDecision_DecidedAt",
                schema: "autofac",
                table: "workflow_run_steps");

            migrationBuilder.DropColumn(
                name: "PolicyDecision_Kind",
                schema: "autofac",
                table: "workflow_run_steps");

            migrationBuilder.DropColumn(
                name: "PolicyDecision_PolicyId",
                schema: "autofac",
                table: "workflow_run_steps");

            migrationBuilder.DropColumn(
                name: "PolicyDecision_PolicyName",
                schema: "autofac",
                table: "workflow_run_steps");

            migrationBuilder.DropColumn(
                name: "PolicyDecision_Rationale",
                schema: "autofac",
                table: "workflow_run_steps");

            migrationBuilder.DropColumn(
                name: "PolicyDecision_RiskFactors",
                schema: "autofac",
                table: "workflow_run_steps");

            migrationBuilder.DropColumn(
                name: "PolicyDecision_RiskLevel",
                schema: "autofac",
                table: "workflow_run_steps");

            migrationBuilder.DropColumn(
                name: "PolicyDecision_RiskScore",
                schema: "autofac",
                table: "workflow_run_steps");
        }
    }
}
