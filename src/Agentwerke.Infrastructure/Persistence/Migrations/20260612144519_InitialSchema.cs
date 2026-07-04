using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agentwerke.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_approval_requests_workflow_runs_WorkflowRunId",
                schema: "agentwerke",
                table: "approval_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_workflow_events_workflow_runs_WorkflowRunId",
                schema: "agentwerke",
                table: "workflow_events");

            migrationBuilder.DropForeignKey(
                name: "FK_workflow_runs_workflow_definitions_WorkflowDefinitionId",
                schema: "agentwerke",
                table: "workflow_runs");

            migrationBuilder.DropTable(
                name: "agent_sessions",
                schema: "agentwerke");

            migrationBuilder.DropTable(
                name: "policy_decisions",
                schema: "agentwerke");

            migrationBuilder.DropIndex(
                name: "IX_workflow_runs_Status",
                schema: "agentwerke",
                table: "workflow_runs");

            migrationBuilder.DropIndex(
                name: "IX_workflow_runs_WorkflowDefinitionId",
                schema: "agentwerke",
                table: "workflow_runs");

            migrationBuilder.DropIndex(
                name: "IX_workflow_events_CreatedAtUtc",
                schema: "agentwerke",
                table: "workflow_events");

            migrationBuilder.DropIndex(
                name: "IX_workflow_events_WorkflowRunId",
                schema: "agentwerke",
                table: "workflow_events");

            migrationBuilder.DropIndex(
                name: "IX_approval_requests_Status",
                schema: "agentwerke",
                table: "approval_requests");

            migrationBuilder.DropIndex(
                name: "IX_approval_requests_WorkflowRunId",
                schema: "agentwerke",
                table: "approval_requests");

            migrationBuilder.DropPrimaryKey(
                name: "PK_workflow_definitions",
                schema: "agentwerke",
                table: "workflow_definitions");

            migrationBuilder.DropIndex(
                name: "IX_workflow_definitions_WorkflowKey_Version",
                schema: "agentwerke",
                table: "workflow_definitions");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                schema: "agentwerke",
                table: "workflow_runs");

            migrationBuilder.DropColumn(
                name: "Initiator",
                schema: "agentwerke",
                table: "workflow_runs");

            migrationBuilder.DropColumn(
                name: "StartedAtUtc",
                schema: "agentwerke",
                table: "workflow_runs");

            migrationBuilder.DropColumn(
                name: "WorkflowDefinitionId",
                schema: "agentwerke",
                table: "workflow_runs");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                schema: "agentwerke",
                table: "workflow_events");

            migrationBuilder.DropColumn(
                name: "PayloadJson",
                schema: "agentwerke",
                table: "workflow_events");

            migrationBuilder.DropColumn(
                name: "WorkflowRunId",
                schema: "agentwerke",
                table: "workflow_events");

            migrationBuilder.DropColumn(
                name: "RequestedAtUtc",
                schema: "agentwerke",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "ResolvedAtUtc",
                schema: "agentwerke",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "WorkflowRunId",
                schema: "agentwerke",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                schema: "agentwerke",
                table: "workflow_definitions");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                schema: "agentwerke",
                table: "workflow_definitions");

            migrationBuilder.RenameTable(
                name: "workflow_definitions",
                schema: "agentwerke",
                newName: "workflows",
                newSchema: "agentwerke");

            migrationBuilder.RenameColumn(
                name: "EventType",
                schema: "agentwerke",
                table: "workflow_events",
                newName: "Type");

            migrationBuilder.RenameColumn(
                name: "RequestedBy",
                schema: "agentwerke",
                table: "approval_requests",
                newName: "RunId");

            migrationBuilder.RenameColumn(
                name: "ApprovalType",
                schema: "agentwerke",
                table: "approval_requests",
                newName: "Requester");

            migrationBuilder.RenameColumn(
                name: "WorkflowKey",
                schema: "agentwerke",
                table: "workflows",
                newName: "Owner");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "agentwerke",
                table: "workflow_runs",
                type: "text",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "CompletedAt",
                schema: "agentwerke",
                table: "workflow_runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentStep",
                schema: "agentwerke",
                table: "workflow_runs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DurationMs",
                schema: "agentwerke",
                table: "workflow_runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PendingApprovals",
                schema: "agentwerke",
                table: "workflow_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RequestedBy",
                schema: "agentwerke",
                table: "workflow_runs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RiskLevel",
                schema: "agentwerke",
                table: "workflow_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StartedAt",
                schema: "agentwerke",
                table: "workflow_runs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<List<string>>(
                name: "Tags",
                schema: "agentwerke",
                table: "workflow_runs",
                type: "jsonb",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "WorkflowId",
                schema: "agentwerke",
                table: "workflow_runs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WorkflowName",
                schema: "agentwerke",
                table: "workflow_runs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WorkflowVersion",
                schema: "agentwerke",
                table: "workflow_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "agentwerke",
                table: "workflow_events",
                type: "text",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "CreatedAt",
                schema: "agentwerke",
                table: "workflow_events",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Message",
                schema: "agentwerke",
                table: "workflow_events",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RunId",
                schema: "agentwerke",
                table: "workflow_events",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "agentwerke",
                table: "approval_requests",
                type: "text",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "ActionRequested",
                schema: "agentwerke",
                table: "approval_requests",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<List<string>>(
                name: "AffectedSystems",
                schema: "agentwerke",
                table: "approval_requests",
                type: "jsonb",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "AgentName",
                schema: "agentwerke",
                table: "approval_requests",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CreatedAt",
                schema: "agentwerke",
                table: "approval_requests",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DecidedAt",
                schema: "agentwerke",
                table: "approval_requests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecidedBy",
                schema: "agentwerke",
                table: "approval_requests",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionComment",
                schema: "agentwerke",
                table: "approval_requests",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyRationale",
                schema: "agentwerke",
                table: "approval_requests",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                schema: "agentwerke",
                table: "approval_requests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<List<string>>(
                name: "RiskFactors",
                schema: "agentwerke",
                table: "approval_requests",
                type: "jsonb",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "RiskLevel",
                schema: "agentwerke",
                table: "approval_requests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RiskScore",
                schema: "agentwerke",
                table: "approval_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SlaDeadline",
                schema: "agentwerke",
                table: "approval_requests",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WorkflowName",
                schema: "agentwerke",
                table: "approval_requests",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "Version",
                schema: "agentwerke",
                table: "workflows",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "agentwerke",
                table: "workflows",
                type: "text",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "BpmnXml",
                schema: "agentwerke",
                table: "workflows",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CreatedAt",
                schema: "agentwerke",
                table: "workflows",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "agentwerke",
                table: "workflows",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastEditedAt",
                schema: "agentwerke",
                table: "workflows",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<List<string>>(
                name: "Tags",
                schema: "agentwerke",
                table: "workflows",
                type: "jsonb",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "ValidationState",
                schema: "agentwerke",
                table: "workflows",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_workflows",
                schema: "agentwerke",
                table: "workflows",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "workflow_run_steps",
                schema: "agentwerke",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartedAt = table.Column<string>(type: "text", nullable: true),
                    CompletedAt = table.Column<string>(type: "text", nullable: true),
                    AgentName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Output = table.Column<string>(type: "text", nullable: true),
                    RunId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_run_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_run_steps_workflow_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "agentwerke",
                        principalTable: "workflow_runs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_events_RunId",
                schema: "agentwerke",
                table: "workflow_events",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_run_steps_RunId",
                schema: "agentwerke",
                table: "workflow_run_steps",
                column: "RunId");

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_events_workflow_runs_RunId",
                schema: "agentwerke",
                table: "workflow_events",
                column: "RunId",
                principalSchema: "agentwerke",
                principalTable: "workflow_runs",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_workflow_events_workflow_runs_RunId",
                schema: "agentwerke",
                table: "workflow_events");

            migrationBuilder.DropTable(
                name: "workflow_run_steps",
                schema: "agentwerke");

            migrationBuilder.DropIndex(
                name: "IX_workflow_events_RunId",
                schema: "agentwerke",
                table: "workflow_events");

            migrationBuilder.DropPrimaryKey(
                name: "PK_workflows",
                schema: "agentwerke",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                schema: "agentwerke",
                table: "workflow_runs");

            migrationBuilder.DropColumn(
                name: "CurrentStep",
                schema: "agentwerke",
                table: "workflow_runs");

            migrationBuilder.DropColumn(
                name: "DurationMs",
                schema: "agentwerke",
                table: "workflow_runs");

            migrationBuilder.DropColumn(
                name: "PendingApprovals",
                schema: "agentwerke",
                table: "workflow_runs");

            migrationBuilder.DropColumn(
                name: "RequestedBy",
                schema: "agentwerke",
                table: "workflow_runs");

            migrationBuilder.DropColumn(
                name: "RiskLevel",
                schema: "agentwerke",
                table: "workflow_runs");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                schema: "agentwerke",
                table: "workflow_runs");

            migrationBuilder.DropColumn(
                name: "Tags",
                schema: "agentwerke",
                table: "workflow_runs");

            migrationBuilder.DropColumn(
                name: "WorkflowId",
                schema: "agentwerke",
                table: "workflow_runs");

            migrationBuilder.DropColumn(
                name: "WorkflowName",
                schema: "agentwerke",
                table: "workflow_runs");

            migrationBuilder.DropColumn(
                name: "WorkflowVersion",
                schema: "agentwerke",
                table: "workflow_runs");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "agentwerke",
                table: "workflow_events");

            migrationBuilder.DropColumn(
                name: "Message",
                schema: "agentwerke",
                table: "workflow_events");

            migrationBuilder.DropColumn(
                name: "RunId",
                schema: "agentwerke",
                table: "workflow_events");

            migrationBuilder.DropColumn(
                name: "ActionRequested",
                schema: "agentwerke",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "AffectedSystems",
                schema: "agentwerke",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "AgentName",
                schema: "agentwerke",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "agentwerke",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "DecidedAt",
                schema: "agentwerke",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "DecidedBy",
                schema: "agentwerke",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "DecisionComment",
                schema: "agentwerke",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "PolicyRationale",
                schema: "agentwerke",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "Priority",
                schema: "agentwerke",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "RiskFactors",
                schema: "agentwerke",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "RiskLevel",
                schema: "agentwerke",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "RiskScore",
                schema: "agentwerke",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "SlaDeadline",
                schema: "agentwerke",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "WorkflowName",
                schema: "agentwerke",
                table: "approval_requests");

            migrationBuilder.DropColumn(
                name: "BpmnXml",
                schema: "agentwerke",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "agentwerke",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "agentwerke",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "LastEditedAt",
                schema: "agentwerke",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "Tags",
                schema: "agentwerke",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "ValidationState",
                schema: "agentwerke",
                table: "workflows");

            migrationBuilder.RenameTable(
                name: "workflows",
                schema: "agentwerke",
                newName: "workflow_definitions",
                newSchema: "agentwerke");

            migrationBuilder.RenameColumn(
                name: "Type",
                schema: "agentwerke",
                table: "workflow_events",
                newName: "EventType");

            migrationBuilder.RenameColumn(
                name: "RunId",
                schema: "agentwerke",
                table: "approval_requests",
                newName: "RequestedBy");

            migrationBuilder.RenameColumn(
                name: "Requester",
                schema: "agentwerke",
                table: "approval_requests",
                newName: "ApprovalType");

            migrationBuilder.RenameColumn(
                name: "Owner",
                schema: "agentwerke",
                table: "workflow_definitions",
                newName: "WorkflowKey");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                schema: "agentwerke",
                table: "workflow_runs",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAtUtc",
                schema: "agentwerke",
                table: "workflow_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Initiator",
                schema: "agentwerke",
                table: "workflow_runs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedAtUtc",
                schema: "agentwerke",
                table: "workflow_runs",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowDefinitionId",
                schema: "agentwerke",
                table: "workflow_runs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                schema: "agentwerke",
                table: "workflow_events",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                schema: "agentwerke",
                table: "workflow_events",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "PayloadJson",
                schema: "agentwerke",
                table: "workflow_events",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowRunId",
                schema: "agentwerke",
                table: "workflow_events",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                schema: "agentwerke",
                table: "approval_requests",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RequestedAtUtc",
                schema: "agentwerke",
                table: "approval_requests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResolvedAtUtc",
                schema: "agentwerke",
                table: "approval_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowRunId",
                schema: "agentwerke",
                table: "approval_requests",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<int>(
                name: "Version",
                schema: "agentwerke",
                table: "workflow_definitions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                schema: "agentwerke",
                table: "workflow_definitions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                schema: "agentwerke",
                table: "workflow_definitions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAtUtc",
                schema: "agentwerke",
                table: "workflow_definitions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddPrimaryKey(
                name: "PK_workflow_definitions",
                schema: "agentwerke",
                table: "workflow_definitions",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "agent_sessions",
                schema: "agentwerke",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_sessions_workflow_runs_WorkflowRunId",
                        column: x => x.WorkflowRunId,
                        principalSchema: "agentwerke",
                        principalTable: "workflow_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "policy_decisions",
                schema: "agentwerke",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    Decision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvaluatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    PolicyName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policy_decisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_policy_decisions_workflow_runs_WorkflowRunId",
                        column: x => x.WorkflowRunId,
                        principalSchema: "agentwerke",
                        principalTable: "workflow_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_runs_Status",
                schema: "agentwerke",
                table: "workflow_runs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_runs_WorkflowDefinitionId",
                schema: "agentwerke",
                table: "workflow_runs",
                column: "WorkflowDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_events_CreatedAtUtc",
                schema: "agentwerke",
                table: "workflow_events",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_events_WorkflowRunId",
                schema: "agentwerke",
                table: "workflow_events",
                column: "WorkflowRunId");

            migrationBuilder.CreateIndex(
                name: "IX_approval_requests_Status",
                schema: "agentwerke",
                table: "approval_requests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_approval_requests_WorkflowRunId",
                schema: "agentwerke",
                table: "approval_requests",
                column: "WorkflowRunId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_definitions_WorkflowKey_Version",
                schema: "agentwerke",
                table: "workflow_definitions",
                columns: new[] { "WorkflowKey", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_sessions_AgentName_Status",
                schema: "agentwerke",
                table: "agent_sessions",
                columns: new[] { "AgentName", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_sessions_WorkflowRunId",
                schema: "agentwerke",
                table: "agent_sessions",
                column: "WorkflowRunId");

            migrationBuilder.CreateIndex(
                name: "IX_policy_decisions_EvaluatedAtUtc",
                schema: "agentwerke",
                table: "policy_decisions",
                column: "EvaluatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_policy_decisions_WorkflowRunId",
                schema: "agentwerke",
                table: "policy_decisions",
                column: "WorkflowRunId");

            migrationBuilder.AddForeignKey(
                name: "FK_approval_requests_workflow_runs_WorkflowRunId",
                schema: "agentwerke",
                table: "approval_requests",
                column: "WorkflowRunId",
                principalSchema: "agentwerke",
                principalTable: "workflow_runs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_events_workflow_runs_WorkflowRunId",
                schema: "agentwerke",
                table: "workflow_events",
                column: "WorkflowRunId",
                principalSchema: "agentwerke",
                principalTable: "workflow_runs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_runs_workflow_definitions_WorkflowDefinitionId",
                schema: "agentwerke",
                table: "workflow_runs",
                column: "WorkflowDefinitionId",
                principalSchema: "agentwerke",
                principalTable: "workflow_definitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
