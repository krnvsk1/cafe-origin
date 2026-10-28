using Microsoft.EntityFrameworkCore.Migrations;
using Kumashaurma.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Kumashaurma.API.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261026000000_AddSubDishToTechCardLines")]
    public partial class AddSubDishToTechCardLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "stock_item_id",
                table: "tech_card_lines",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "sub_dish_id",
                table: "tech_card_lines",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tech_card_lines_sub_dish_id",
                table: "tech_card_lines",
                column: "sub_dish_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tech_card_lines_component",
                table: "tech_card_lines",
                sql: "(stock_item_id IS NOT NULL AND sub_dish_id IS NULL) OR (stock_item_id IS NULL AND sub_dish_id IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_tech_card_lines_shawarmas_sub_dish_id",
                table: "tech_card_lines",
                column: "sub_dish_id",
                principalTable: "shawarmas",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tech_card_lines_shawarmas_sub_dish_id",
                table: "tech_card_lines");

            migrationBuilder.DropIndex(
                name: "IX_tech_card_lines_sub_dish_id",
                table: "tech_card_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tech_card_lines_component",
                table: "tech_card_lines");

            migrationBuilder.DropColumn(
                name: "sub_dish_id",
                table: "tech_card_lines");

            migrationBuilder.AlterColumn<int>(
                name: "stock_item_id",
                table: "tech_card_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
