using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Felcra.Profiling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFarmLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "latitude",
                table: "farms",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "longitude",
                table: "farms",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "latitude",
                table: "farms");

            migrationBuilder.DropColumn(
                name: "longitude",
                table: "farms");
        }
    }
}
