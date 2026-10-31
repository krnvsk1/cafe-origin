using Cafe.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <summary>
    /// Источник заказа (сайт или касса) и кассир, принявший заказ.
    /// Все существующие заказы — с сайта.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261025120000_AddOrderSource")]
    public partial class AddOrderSource : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cashier_id",
                table: "orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "online");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "cashier_id", table: "orders");
            migrationBuilder.DropColumn(name: "source", table: "orders");
        }
    }
}
