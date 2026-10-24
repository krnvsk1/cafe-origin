using System;
using Kumashaurma.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kumashaurma.API.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261024160000_AddOrderPayment")]
    public partial class AddOrderPayment : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payment_method",
                table: "orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "cash");

            migrationBuilder.AddColumn<string>(
                name: "payment_status",
                table: "orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "unpaid");

            migrationBuilder.AddColumn<string>(
                name: "payment_id",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "paid_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_payment_id",
                table: "orders",
                column: "payment_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_orders_payment_id", table: "orders");
            migrationBuilder.DropColumn(name: "payment_method", table: "orders");
            migrationBuilder.DropColumn(name: "payment_status", table: "orders");
            migrationBuilder.DropColumn(name: "payment_id", table: "orders");
            migrationBuilder.DropColumn(name: "paid_at", table: "orders");
        }
    }
}
