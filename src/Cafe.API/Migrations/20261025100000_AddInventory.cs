using System;
using Cafe.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <summary>
    /// Склад и техкарты: номенклатура, поставщики, документы, журнал движений, технологические карты;
    /// отметка о списании продуктов по заказу.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261025100000_AddInventory")]
    public partial class AddInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "stock_deducted_at",
                table: "orders",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "stock_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(14,3)", nullable: false),
                    avg_cost = table.Column<decimal>(type: "numeric(14,4)", nullable: false),
                    last_price = table.Column<decimal>(type: "numeric(14,4)", nullable: false),
                    min_quantity = table.Column<decimal>(type: "numeric(14,3)", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    inn = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    contact_person = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    stock_item_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(14,3)", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(14,4)", nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(14,3)", nullable: false),
                    document_id = table.Column<int>(type: "integer", nullable: true),
                    order_id = table.Column<int>(type: "integer", nullable: true),
                    comment = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movements", x => x.id);
                    table.ForeignKey(
                        name: "FK_stock_movements_stock_items_stock_item_id",
                        column: x => x.stock_item_id,
                        principalTable: "stock_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tech_cards",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    shawarma_id = table.Column<int>(type: "integer", nullable: true),
                    addon_id = table.Column<int>(type: "integer", nullable: true),
                    stock_item_id = table.Column<int>(type: "integer", nullable: true),
                    output_weight = table.Column<decimal>(type: "numeric(10,1)", nullable: true),
                    yield_quantity = table.Column<decimal>(type: "numeric(14,3)", nullable: false),
                    instructions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tech_cards", x => x.id);
                    table.ForeignKey(
                        name: "FK_tech_cards_addons_addon_id",
                        column: x => x.addon_id,
                        principalTable: "addons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tech_cards_shawarmas_shawarma_id",
                        column: x => x.shawarma_id,
                        principalTable: "shawarmas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tech_cards_stock_items_stock_item_id",
                        column: x => x.stock_item_id,
                        principalTable: "stock_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_documents",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    supplier_id = table.Column<int>(type: "integer", nullable: true),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    total = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    attachment_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    posted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_documents", x => x.id);
                    table.ForeignKey(
                        name: "FK_stock_documents_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tech_card_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tech_card_id = table.Column<int>(type: "integer", nullable: false),
                    stock_item_id = table.Column<int>(type: "integer", nullable: false),
                    gross = table.Column<decimal>(type: "numeric(14,4)", nullable: false),
                    loss_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tech_card_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_tech_card_lines_stock_items_stock_item_id",
                        column: x => x.stock_item_id,
                        principalTable: "stock_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tech_card_lines_tech_cards_tech_card_id",
                        column: x => x.tech_card_id,
                        principalTable: "tech_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_document_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    document_id = table.Column<int>(type: "integer", nullable: false),
                    stock_item_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(14,3)", nullable: false),
                    price = table.Column<decimal>(type: "numeric(14,4)", nullable: false),
                    sum = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    expected_quantity = table.Column<decimal>(type: "numeric(14,3)", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_document_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_stock_document_lines_stock_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "stock_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_stock_document_lines_stock_items_stock_item_id",
                        column: x => x.stock_item_id,
                        principalTable: "stock_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stock_document_lines_document_id",
                table: "stock_document_lines",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_document_lines_stock_item_id",
                table: "stock_document_lines",
                column: "stock_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_supplier_id",
                table: "stock_documents",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_documents_type_date",
                table: "stock_documents",
                columns: new[] { "type", "date" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_items_name",
                table: "stock_items",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_document_id",
                table: "stock_movements",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_order_id",
                table: "stock_movements",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_stock_item_id_created_at",
                table: "stock_movements",
                columns: new[] { "stock_item_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_type_created_at",
                table: "stock_movements",
                columns: new[] { "type", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_tech_card_lines_stock_item_id",
                table: "tech_card_lines",
                column: "stock_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_tech_card_lines_tech_card_id",
                table: "tech_card_lines",
                column: "tech_card_id");

            migrationBuilder.CreateIndex(
                name: "IX_tech_cards_addon_id",
                table: "tech_cards",
                column: "addon_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tech_cards_shawarma_id",
                table: "tech_cards",
                column: "shawarma_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tech_cards_stock_item_id",
                table: "tech_cards",
                column: "stock_item_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_document_lines");

            migrationBuilder.DropTable(
                name: "stock_movements");

            migrationBuilder.DropTable(
                name: "tech_card_lines");

            migrationBuilder.DropTable(
                name: "stock_documents");

            migrationBuilder.DropTable(
                name: "tech_cards");

            migrationBuilder.DropTable(
                name: "suppliers");

            migrationBuilder.DropTable(
                name: "stock_items");

            migrationBuilder.DropColumn(
                name: "stock_deducted_at",
                table: "orders");
        }
    }
}
