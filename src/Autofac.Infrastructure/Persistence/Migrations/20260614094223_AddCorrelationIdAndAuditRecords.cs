using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autofac.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCorrelationIdAndAuditRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                schema: "autofac",
                table: "workflow_runs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "audit_records",
                schema: "autofac",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RunId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ResourceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Details = table.Column<string>(type: "text", nullable: true),
                    Timestamp = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_records", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_records",
                schema: "autofac");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                schema: "autofac",
                table: "workflow_runs");
        }
    }
}
