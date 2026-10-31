using System;
using Cafe.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261028200000_AddFiscalReceipts")]
    public partial class AddFiscalReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fiscal_receipts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    request_uuid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    total = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    client_contact = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    fiscal_document_number = table.Column<long>(type: "bigint", nullable: true),
                    fiscal_document_sign = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    fn_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    shift_number = table.Column<int>(type: "integer", nullable: true),
                    receipt_number = table.Column<int>(type: "integer", nullable: true),
                    fiscal_date_time = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    request_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_receipts", x => x.id);
                    table.ForeignKey(
                        name: "FK_fiscal_receipts_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_receipts_order_id",
                table: "fiscal_receipts",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_receipts_status",
                table: "fiscal_receipts",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fiscal_receipts");
        }
    }
}
