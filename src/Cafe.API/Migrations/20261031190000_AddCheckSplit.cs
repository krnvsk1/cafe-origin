using Cafe.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <summary>Несколько счетов за столом и деление счёта: подпись счёта, счёт у позиции заказа</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261031190000_AddCheckSplit")]
    public partial class AddCheckSplit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "label",
                table: "table_checks",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "check_id",
                table: "order_items",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_items_check_id",
                table: "order_items",
                column: "check_id");

            migrationBuilder.AddForeignKey(
                name: "FK_order_items_table_checks_check_id",
                table: "order_items",
                column: "check_id",
                principalTable: "table_checks",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_order_items_table_checks_check_id",
                table: "order_items");

            migrationBuilder.DropIndex(
                name: "IX_order_items_check_id",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "check_id",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "label",
                table: "table_checks");
        }
    }
}
