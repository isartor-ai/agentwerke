using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agentwerke.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInteractionChannelsAndDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelledAt",
                schema: "agentwerke",
                table: "agent_interactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledBy",
                schema: "agentwerke",
                table: "agent_interactions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultAnswer",
                schema: "agentwerke",
                table: "agent_interactions",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DelegationDepth",
                schema: "agentwerke",
                table: "agent_interactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ExpiresAction",
                schema: "agentwerke",
                table: "agent_interactions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedChannels",
                schema: "agentwerke",
                table: "agent_interactions",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "RespondedChannel",
                schema: "agentwerke",
                table: "agent_interactions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResumedAt",
                schema: "agentwerke",
                table: "agent_interactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeoutAt",
                schema: "agentwerke",
                table: "agent_interactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                schema: "agentwerke",
                table: "agent_interactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "interaction_deliveries",
                schema: "agentwerke",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    InteractionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChannelMessageId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interaction_deliveries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_interactions_CorrelationId",
                schema: "agentwerke",
                table: "agent_interactions",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_interactions_Status",
                schema: "agentwerke",
                table: "agent_interactions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_agent_interactions_Status_TimeoutAt",
                schema: "agentwerke",
                table: "agent_interactions",
                columns: new[] { "Status", "TimeoutAt" });

            migrationBuilder.CreateIndex(
                name: "IX_interaction_deliveries_Channel_ChannelMessageId",
                schema: "agentwerke",
                table: "interaction_deliveries",
                columns: new[] { "Channel", "ChannelMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_interaction_deliveries_InteractionId",
                schema: "agentwerke",
                table: "interaction_deliveries",
                column: "InteractionId");

            migrationBuilder.CreateIndex(
                name: "IX_interaction_deliveries_InteractionId_Channel",
                schema: "agentwerke",
                table: "interaction_deliveries",
                columns: new[] { "InteractionId", "Channel" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "interaction_deliveries",
                schema: "agentwerke");

            migrationBuilder.DropIndex(
                name: "IX_agent_interactions_CorrelationId",
                schema: "agentwerke",
                table: "agent_interactions");

            migrationBuilder.DropIndex(
                name: "IX_agent_interactions_Status",
                schema: "agentwerke",
                table: "agent_interactions");

            migrationBuilder.DropIndex(
                name: "IX_agent_interactions_Status_TimeoutAt",
                schema: "agentwerke",
                table: "agent_interactions");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                schema: "agentwerke",
                table: "agent_interactions");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                schema: "agentwerke",
                table: "agent_interactions");

            migrationBuilder.DropColumn(
                name: "DefaultAnswer",
                schema: "agentwerke",
                table: "agent_interactions");

            migrationBuilder.DropColumn(
                name: "DelegationDepth",
                schema: "agentwerke",
                table: "agent_interactions");

            migrationBuilder.DropColumn(
                name: "ExpiresAction",
                schema: "agentwerke",
                table: "agent_interactions");

            migrationBuilder.DropColumn(
                name: "RequestedChannels",
                schema: "agentwerke",
                table: "agent_interactions");

            migrationBuilder.DropColumn(
                name: "RespondedChannel",
                schema: "agentwerke",
                table: "agent_interactions");

            migrationBuilder.DropColumn(
                name: "ResumedAt",
                schema: "agentwerke",
                table: "agent_interactions");

            migrationBuilder.DropColumn(
                name: "TimeoutAt",
                schema: "agentwerke",
                table: "agent_interactions");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "agentwerke",
                table: "agent_interactions");
        }
    }
}
