using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Kumashaurma.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kumashaurma.API.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261025200000_AddEdoDocuments")]
    public partial class AddEdoDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "edo_documents",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sbis_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    supplier_inn = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    supplier_kpp = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    supplier_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    total = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    lines_count = table.Column<int>(type: "integer", nullable: false),
                    codes_count = table.Column<int>(type: "integer", nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    stock_document_id = table.Column<int>(type: "integer", nullable: true),
                    received_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    imported_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    imported_by_user_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edo_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "edo_item_mappings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    supplier_inn = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    item_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    item_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    stock_item_id = table.Column<int>(type: "integer", nullable: false),
                    factor = table.Column<decimal>(type: "numeric(14,6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edo_item_mappings", x => x.id);
                    table.ForeignKey(
                        name: "FK_edo_item_mappings_stock_items_stock_item_id",
                        column: x => x.stock_item_id,
                        principalTable: "stock_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_edo_documents_date",
                table: "edo_documents",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "IX_edo_documents_sbis_id",
                table: "edo_documents",
                column: "sbis_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_edo_documents_status",
                table: "edo_documents",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_edo_item_mappings_stock_item_id",
                table: "edo_item_mappings",
                column: "stock_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_edo_item_mappings_supplier_inn_item_key",
                table: "edo_item_mappings",
                columns: new[] { "supplier_inn", "item_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "edo_documents");

            migrationBuilder.DropTable(
                name: "edo_item_mappings");
        }
    }
}
