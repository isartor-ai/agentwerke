using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agentwerke.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalEventDeliveryIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryId",
                schema: "agentwerke",
                table: "external_workflow_events",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                schema: "agentwerke",
                table: "external_workflow_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_workflow_events_delivery_id",
                schema: "agentwerke",
                table: "external_workflow_events",
                column: "DeliveryId",
                unique: true,
                filter: "\"DeliveryId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_external_workflow_events_delivery_id",
                schema: "agentwerke",
                table: "external_workflow_events");

            migrationBuilder.DropColumn(
                name: "DeliveryId",
                schema: "agentwerke",
                table: "external_workflow_events");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "agentwerke",
                table: "external_workflow_events");
        }
    }
}
