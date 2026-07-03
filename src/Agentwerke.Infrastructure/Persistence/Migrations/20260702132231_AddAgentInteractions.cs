using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agentwerke.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentInteractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_interactions",
                schema: "agentwerke",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RunId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StepId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FromAgent = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AddresseeType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Addressee = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Blocking = table.Column<bool>(type: "boolean", nullable: false),
                    Prompt = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    Options = table.Column<string>(type: "jsonb", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Response = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    RespondedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RespondedAt = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_interactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_interactions_RunId",
                schema: "agentwerke",
                table: "agent_interactions",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_interactions",
                schema: "agentwerke");
        }
    }
}
