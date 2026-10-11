using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Felcra.Profiling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledReportRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scheduled_report_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    emails_sent = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_report_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_report_runs_period",
                table: "scheduled_report_runs",
                column: "period",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scheduled_report_runs");
        }
    }
}
