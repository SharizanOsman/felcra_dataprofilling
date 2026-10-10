using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Felcra.Profiling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnimalsAndCensus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "animals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    animal_no = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    farm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    herd_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    breed_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tag_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    sex = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: true),
                    birth_date_method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    dam_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sire_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    remarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("pk_animals", x => x.id);
                    table.ForeignKey(
                        name: "fk_animals_animals_dam_id",
                        column: x => x.dam_id,
                        principalTable: "animals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_animals_animals_sire_id",
                        column: x => x.sire_id,
                        principalTable: "animals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_animals_breeds_breed_id",
                        column: x => x.breed_id,
                        principalTable: "breeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_animals_farms_farm_id",
                        column: x => x.farm_id,
                        principalTable: "farms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_animals_herd_groups_herd_group_id",
                        column: x => x.herd_group_id,
                        principalTable: "herd_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_animals_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "census_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    farm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    pic_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    pic_position = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    pic_phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    recorded_bulls = table.Column<int>(type: "integer", nullable: true),
                    recorded_dams = table.Column<int>(type: "integer", nullable: true),
                    recorded_male_calves = table.Column<int>(type: "integer", nullable: true),
                    recorded_female_calves = table.Column<int>(type: "integer", nullable: true),
                    is_closed = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_census_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_census_sessions_farms_farm_id",
                        column: x => x.farm_id,
                        principalTable: "farms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "animal_tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    animal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    assigned_on = table.Column<DateOnly>(type: "date", nullable: false),
                    removed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_animal_tags", x => x.id);
                    table.ForeignKey(
                        name: "fk_animal_tags_animals_animal_id",
                        column: x => x.animal_id,
                        principalTable: "animals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "census_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    census_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    animal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    farm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: true),
                    category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    estimated_age_months = table.Column<int>(type: "integer", nullable: true),
                    weight_kg = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    body_condition_score = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    selected = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_census_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_census_entries_animals_animal_id",
                        column: x => x.animal_id,
                        principalTable: "animals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_census_entries_census_sessions_census_session_id",
                        column: x => x.census_session_id,
                        principalTable: "census_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "census_session_treatments",
                columns: table => new
                {
                    census_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    treatment_product_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_census_session_treatments", x => new { x.census_session_id, x.treatment_product_id });
                    table.ForeignKey(
                        name: "fk_census_session_treatments_census_sessions_census_session_id",
                        column: x => x.census_session_id,
                        principalTable: "census_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_census_session_treatments_treatment_products_treatment_prod",
                        column: x => x.treatment_product_id,
                        principalTable: "treatment_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_animal_tags_animal_id",
                table: "animal_tags",
                column: "animal_id");

            migrationBuilder.CreateIndex(
                name: "ix_animal_tags_tag_number",
                table: "animal_tags",
                column: "tag_number");

            migrationBuilder.CreateIndex(
                name: "ix_animals_animal_no",
                table: "animals",
                column: "animal_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_animals_breed_id",
                table: "animals",
                column: "breed_id");

            migrationBuilder.CreateIndex(
                name: "ix_animals_dam_id",
                table: "animals",
                column: "dam_id");

            migrationBuilder.CreateIndex(
                name: "ix_animals_farm_id_tag_number",
                table: "animals",
                columns: new[] { "farm_id", "tag_number" });

            migrationBuilder.CreateIndex(
                name: "ix_animals_herd_group_id",
                table: "animals",
                column: "herd_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_animals_project_id",
                table: "animals",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_animals_sire_id",
                table: "animals",
                column: "sire_id");

            migrationBuilder.CreateIndex(
                name: "ix_census_entries_animal_id",
                table: "census_entries",
                column: "animal_id");

            migrationBuilder.CreateIndex(
                name: "ix_census_entries_census_session_id_animal_id",
                table: "census_entries",
                columns: new[] { "census_session_id", "animal_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_census_session_treatments_treatment_product_id",
                table: "census_session_treatments",
                column: "treatment_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_census_sessions_farm_id_start_date",
                table: "census_sessions",
                columns: new[] { "farm_id", "start_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "animal_tags");

            migrationBuilder.DropTable(
                name: "census_entries");

            migrationBuilder.DropTable(
                name: "census_session_treatments");

            migrationBuilder.DropTable(
                name: "animals");

            migrationBuilder.DropTable(
                name: "census_sessions");
        }
    }
}
