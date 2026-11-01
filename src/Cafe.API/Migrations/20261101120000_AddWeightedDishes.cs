using Cafe.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <summary>Весовые блюда: признак у блюда (цена за 100 г) и вес в граммах у позиции заказа</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261101120000_AddWeightedDishes")]
    public partial class AddWeightedDishes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_weighted",
                table: "dishes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "weight",
                table: "order_items",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "weight", table: "order_items");
            migrationBuilder.DropColumn(name: "is_weighted", table: "dishes");
        }
    }
}
