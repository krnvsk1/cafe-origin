using System;
using Kumashaurma.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kumashaurma.API.Migrations
{
    /// <summary>Журнал отменённых блюд (официант убрал из чека, кухня подтвердила со списанием или без)</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261031200000_AddOrderItemVoids")]
    public partial class AddOrderItemVoids : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_item_voids",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<int>(type: "integer", nullable: false),
                    order_item_id = table.Column<int>(type: "integer", nullable: true),
                    check_id = table.Column<int>(type: "integer", nullable: true),
                    shawarma_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    order_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    write_off = table.Column<bool>(type: "boolean", nullable: true),
                    requested_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    requested_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    resolved_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_item_voids", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_item_voids_order_id",
                table: "order_item_voids",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_item_voids_requested_at",
                table: "order_item_voids",
                column: "requested_at");

            migrationBuilder.CreateIndex(
                name: "IX_order_item_voids_status",
                table: "order_item_voids",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_item_voids");
        }
    }
}
