using System;
using Kumashaurma.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kumashaurma.API.Migrations
{
    /// <summary>
    /// ЕГАИС: входящие накладные и их строки, код ЕГАИС у позиции склада;
    /// способы получения у позиций меню (пиво — только в зале).
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261025140000_AddEgaisAndOrderTypes")]
    public partial class AddEgaisAndOrderTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "egais_alc_code",
                table: "stock_items",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "order_types",
                table: "shawarmas",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "egais_documents",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    doc_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    identity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    shipper_fsrar_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    shipper_inn = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    shipper_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    wb_reg_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    act_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    act_decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    act_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    act_sent_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    result_comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    result_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    stock_document_id = table.Column<int>(type: "integer", nullable: true),
                    raw_xml = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_egais_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "egais_document_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    document_id = table.Column<int>(type: "integer", nullable: false),
                    identity = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    product_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    alc_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    capacity = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    alc_volume = table.Column<decimal>(type: "numeric(6,3)", nullable: true),
                    unit_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(14,4)", nullable: false),
                    price = table.Column<decimal>(type: "numeric(14,4)", nullable: false),
                    inform_f2_reg_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    real_quantity = table.Column<decimal>(type: "numeric(14,4)", nullable: true),
                    stock_item_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_egais_document_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_egais_document_lines_egais_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "egais_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_egais_document_lines_stock_items_stock_item_id",
                        column: x => x.stock_item_id,
                        principalTable: "stock_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stock_items_egais_alc_code",
                table: "stock_items",
                column: "egais_alc_code");

            migrationBuilder.CreateIndex(
                name: "IX_egais_document_lines_document_id",
                table: "egais_document_lines",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_egais_document_lines_stock_item_id",
                table: "egais_document_lines",
                column: "stock_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_egais_documents_shipper_fsrar_id_number_date",
                table: "egais_documents",
                columns: new[] { "shipper_fsrar_id", "number", "date" });

            migrationBuilder.CreateIndex(
                name: "IX_egais_documents_status",
                table: "egais_documents",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_egais_documents_wb_reg_id",
                table: "egais_documents",
                column: "wb_reg_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "egais_document_lines");

            migrationBuilder.DropTable(
                name: "egais_documents");

            migrationBuilder.DropIndex(
                name: "IX_stock_items_egais_alc_code",
                table: "stock_items");

            migrationBuilder.DropColumn(
                name: "egais_alc_code",
                table: "stock_items");

            migrationBuilder.DropColumn(
                name: "order_types",
                table: "shawarmas");
        }
    }
}
