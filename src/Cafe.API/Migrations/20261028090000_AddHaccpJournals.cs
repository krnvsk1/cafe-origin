using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Cafe.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261028090000_AddHaccpJournals")]
    public partial class AddHaccpJournals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "haccp_objects",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    min_value = table.Column<decimal>(type: "numeric(6,1)", nullable: true),
                    max_value = table.Column<decimal>(type: "numeric(6,1)", nullable: true),
                    min_humidity = table.Column<decimal>(type: "numeric(5,1)", nullable: true),
                    max_humidity = table.Column<decimal>(type: "numeric(5,1)", nullable: true),
                    checks_per_day = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_haccp_objects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "haccp_staff",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    position = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    medical_book_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    medical_exam_until = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    hygiene_training_until = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    user_id = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_haccp_staff", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "haccp_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    journal = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    object_id = table.Column<int>(type: "integer", nullable: true),
                    staff_id = table.Column<int>(type: "integer", nullable: true),
                    stock_document_id = table.Column<int>(type: "integer", nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    has_deviation = table.Column<bool>(type: "boolean", nullable: false),
                    deviation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    corrective_action = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    is_annulled = table.Column<bool>(type: "boolean", nullable: false),
                    annul_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_haccp_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_haccp_entries_haccp_objects_object_id",
                        column: x => x.object_id,
                        principalTable: "haccp_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_haccp_entries_haccp_staff_staff_id",
                        column: x => x.staff_id,
                        principalTable: "haccp_staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "haccp_entry_revisions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entry_id = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    corrective_action = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    changed_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    changed_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    changed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_haccp_entry_revisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_haccp_entry_revisions_haccp_entries_entry_id",
                        column: x => x.entry_id,
                        principalTable: "haccp_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_haccp_entries_journal_occurred_at",
                table: "haccp_entries",
                columns: new[] { "journal", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_haccp_entries_object_id",
                table: "haccp_entries",
                column: "object_id");

            migrationBuilder.CreateIndex(
                name: "IX_haccp_entries_staff_id",
                table: "haccp_entries",
                column: "staff_id");

            migrationBuilder.CreateIndex(
                name: "IX_haccp_entries_stock_document_id",
                table: "haccp_entries",
                column: "stock_document_id");

            migrationBuilder.CreateIndex(
                name: "IX_haccp_entry_revisions_entry_id",
                table: "haccp_entry_revisions",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "IX_haccp_objects_kind_is_active",
                table: "haccp_objects",
                columns: new[] { "kind", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "haccp_entry_revisions");

            migrationBuilder.DropTable(
                name: "haccp_entries");

            migrationBuilder.DropTable(
                name: "haccp_objects");

            migrationBuilder.DropTable(
                name: "haccp_staff");
        }
    }
}
