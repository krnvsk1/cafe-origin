using Cafe.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <summary>Инвентаризация с полуфабрикатами: сколько товара нашлось в пересчитанных п/ф</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261101150000_AddInventorySemiQuantity")]
    public partial class AddInventorySemiQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "semi_quantity",
                table: "stock_document_lines",
                type: "decimal(14,3)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "semi_quantity", table: "stock_document_lines");
        }
    }
}
