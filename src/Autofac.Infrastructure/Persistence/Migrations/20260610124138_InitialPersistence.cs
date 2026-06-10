using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autofac.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "autofac");

            migrationBuilder.CreateTable(
                name: "workflow_definitions",
                schema: "autofac",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_runs",
                schema: "autofac",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Initiator = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_runs_workflow_definitions_WorkflowDefinitionId",
                        column: x => x.WorkflowDefinitionId,
                        principalSchema: "autofac",
                        principalTable: "workflow_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_sessions",
                schema: "autofac",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_sessions_workflow_runs_WorkflowRunId",
                        column: x => x.WorkflowRunId,
                        principalSchema: "autofac",
                        principalTable: "workflow_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "approval_requests",
                schema: "autofac",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovalType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_approval_requests_workflow_runs_WorkflowRunId",
                        column: x => x.WorkflowRunId,
                        principalSchema: "autofac",
                        principalTable: "workflow_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "policy_decisions",
                schema: "autofac",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    PolicyName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Decision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    EvaluatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policy_decisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_policy_decisions_workflow_runs_WorkflowRunId",
                        column: x => x.WorkflowRunId,
                        principalSchema: "autofac",
                        principalTable: "workflow_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "workflow_events",
                schema: "autofac",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_events_workflow_runs_WorkflowRunId",
                        column: x => x.WorkflowRunId,
                        principalSchema: "autofac",
                        principalTable: "workflow_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_sessions_AgentName_Status",
                schema: "autofac",
                table: "agent_sessions",
                columns: new[] { "AgentName", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_sessions_WorkflowRunId",
                schema: "autofac",
                table: "agent_sessions",
                column: "WorkflowRunId");

            migrationBuilder.CreateIndex(
                name: "IX_approval_requests_Status",
                schema: "autofac",
                table: "approval_requests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_approval_requests_WorkflowRunId",
                schema: "autofac",
                table: "approval_requests",
                column: "WorkflowRunId");

            migrationBuilder.CreateIndex(
                name: "IX_policy_decisions_EvaluatedAtUtc",
                schema: "autofac",
                table: "policy_decisions",
                column: "EvaluatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_policy_decisions_WorkflowRunId",
                schema: "autofac",
                table: "policy_decisions",
                column: "WorkflowRunId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_definitions_WorkflowKey_Version",
                schema: "autofac",
                table: "workflow_definitions",
                columns: new[] { "WorkflowKey", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_events_CreatedAtUtc",
                schema: "autofac",
                table: "workflow_events",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_events_WorkflowRunId",
                schema: "autofac",
                table: "workflow_events",
                column: "WorkflowRunId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_runs_Status",
                schema: "autofac",
                table: "workflow_runs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_runs_WorkflowDefinitionId",
                schema: "autofac",
                table: "workflow_runs",
                column: "WorkflowDefinitionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_sessions",
                schema: "autofac");

            migrationBuilder.DropTable(
                name: "approval_requests",
                schema: "autofac");

            migrationBuilder.DropTable(
                name: "policy_decisions",
                schema: "autofac");

            migrationBuilder.DropTable(
                name: "workflow_events",
                schema: "autofac");

            migrationBuilder.DropTable(
                name: "workflow_runs",
                schema: "autofac");

            migrationBuilder.DropTable(
                name: "workflow_definitions",
                schema: "autofac");
        }
    }
}
