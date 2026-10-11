using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Felcra.Profiling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBreeding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "birth_event_id",
                table: "animals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "breeding_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    animal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    farm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    event_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sire_id = table.Column<Guid>(type: "uuid", nullable: true),
                    semen_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    pregnancy_result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    gestation_days = table.Column<int>(type: "integer", nullable: true),
                    assisted = table.Column<bool>(type: "boolean", nullable: true),
                    stillborn_count = table.Column<int>(type: "integer", nullable: true),
                    soundness_result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_breeding_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_breeding_events_animals_animal_id",
                        column: x => x.animal_id,
                        principalTable: "animals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_breeding_events_animals_sire_id",
                        column: x => x.sire_id,
                        principalTable: "animals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_animals_birth_event_id",
                table: "animals",
                column: "birth_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_breeding_events_animal_id_event_date",
                table: "breeding_events",
                columns: new[] { "animal_id", "event_date" });

            migrationBuilder.CreateIndex(
                name: "ix_breeding_events_sire_id",
                table: "breeding_events",
                column: "sire_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "breeding_events");

            migrationBuilder.DropIndex(
                name: "ix_animals_birth_event_id",
                table: "animals");

            migrationBuilder.DropColumn(
                name: "birth_event_id",
                table: "animals");
        }
    }
}
