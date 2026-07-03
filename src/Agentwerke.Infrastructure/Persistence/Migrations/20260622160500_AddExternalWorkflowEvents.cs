using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agentwerke.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalWorkflowEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "external_workflow_events",
                schema: "autofac",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CorrelationHint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    ReceivedAt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_workflow_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_external_workflow_events_correlation_hint",
                schema: "autofac",
                table: "external_workflow_events",
                column: "CorrelationHint");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_workflow_events",
                schema: "autofac");
        }
    }
}
