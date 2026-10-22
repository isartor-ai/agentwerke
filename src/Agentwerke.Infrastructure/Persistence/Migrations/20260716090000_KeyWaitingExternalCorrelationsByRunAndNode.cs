using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agentwerke.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KeyWaitingExternalCorrelationsByRunAndNode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_waiting_external_correlations",
                schema: "agentwerke",
                table: "waiting_external_correlations");

            migrationBuilder.AddColumn<string>(
                name: "NodeId",
                schema: "agentwerke",
                table: "waiting_external_correlations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_waiting_external_correlations",
                schema: "agentwerke",
                table: "waiting_external_correlations",
                columns: new[] { "RunId", "NodeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_waiting_external_correlations",
                schema: "agentwerke",
                table: "waiting_external_correlations");

            migrationBuilder.DropColumn(
                name: "NodeId",
                schema: "agentwerke",
                table: "waiting_external_correlations");

            migrationBuilder.AddPrimaryKey(
                name: "PK_waiting_external_correlations",
                schema: "agentwerke",
                table: "waiting_external_correlations",
                column: "RunId");
        }
    }
}
