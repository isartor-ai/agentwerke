using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Autofac.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWaitingExternalCorrelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "waiting_external_correlations",
                schema: "autofac",
                columns: table => new
                {
                    RunId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CorrelationKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MessageName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_waiting_external_correlations", x => x.RunId);
                });

            migrationBuilder.CreateIndex(
                name: "ix_waiting_external_correlations_lookup",
                schema: "autofac",
                table: "waiting_external_correlations",
                columns: new[] { "MessageName", "CorrelationKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "waiting_external_correlations",
                schema: "autofac");
        }
    }
}
