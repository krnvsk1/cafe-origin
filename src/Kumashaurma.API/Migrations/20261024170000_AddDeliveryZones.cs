using System;
using Kumashaurma.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kumashaurma.API.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261024170000_AddDeliveryZones")]
    public partial class AddDeliveryZones : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "delivery_zones",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    polygon = table.Column<string>(type: "text", nullable: false),
                    delivery_price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    min_order_amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    free_delivery_from = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    delivery_minutes = table.Column<int>(type: "integer", nullable: false),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_zones", x => x.id);
                });

            migrationBuilder.AddColumn<int>(name: "delivery_zone_id", table: "orders", type: "integer", nullable: true);
            migrationBuilder.AddColumn<double>(name: "latitude", table: "orders", type: "double precision", nullable: true);
            migrationBuilder.AddColumn<double>(name: "longitude", table: "orders", type: "double precision", nullable: true);
            migrationBuilder.AddColumn<decimal>(name: "delivery_price", table: "orders", type: "numeric(10,2)", nullable: false, defaultValue: 0m);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "delivery_zones");
            migrationBuilder.DropColumn(name: "delivery_zone_id", table: "orders");
            migrationBuilder.DropColumn(name: "latitude", table: "orders");
            migrationBuilder.DropColumn(name: "longitude", table: "orders");
            migrationBuilder.DropColumn(name: "delivery_price", table: "orders");
        }
    }
}
