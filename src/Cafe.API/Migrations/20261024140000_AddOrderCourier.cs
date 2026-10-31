using System;
using Cafe.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cafe.API.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261024140000_AddOrderCourier")]
    public partial class AddOrderCourier : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "courier_id",
                table: "orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "picked_up_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_courier_id",
                table: "orders",
                column: "courier_id");

            migrationBuilder.AddForeignKey(
                name: "FK_orders_users_courier_id",
                table: "orders",
                column: "courier_id",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_orders_users_courier_id", table: "orders");
            migrationBuilder.DropIndex(name: "IX_orders_courier_id", table: "orders");
            migrationBuilder.DropColumn(name: "courier_id", table: "orders");
            migrationBuilder.DropColumn(name: "picked_up_at", table: "orders");
        }
    }
}
