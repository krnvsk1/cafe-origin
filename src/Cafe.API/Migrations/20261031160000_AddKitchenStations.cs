using System;
using Cafe.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <summary>Раздельные кухни: кухни, метка кухни у блюда, статус части заказа на каждой кухне</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261031160000_AddKitchenStations")]
    public partial class AddKitchenStations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "kitchen_stations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kitchen_stations", x => x.id);
                });

            migrationBuilder.AddColumn<int>(
                name: "kitchen_station_id",
                table: "shawarmas",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "order_station_statuses",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_id = table.Column<int>(type: "integer", nullable: false),
                    station_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_by_user_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_station_statuses", x => x.id);
                    table.ForeignKey(
                        name: "FK_order_station_statuses_kitchen_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "kitchen_stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_order_station_statuses_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shawarmas_kitchen_station_id",
                table: "shawarmas",
                column: "kitchen_station_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_station_statuses_order_id_station_id",
                table: "order_station_statuses",
                columns: new[] { "order_id", "station_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_station_statuses_station_id",
                table: "order_station_statuses",
                column: "station_id");

            migrationBuilder.AddForeignKey(
                name: "FK_shawarmas_kitchen_stations_kitchen_station_id",
                table: "shawarmas",
                column: "kitchen_station_id",
                principalTable: "kitchen_stations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_shawarmas_kitchen_stations_kitchen_station_id",
                table: "shawarmas");

            migrationBuilder.DropTable(
                name: "order_station_statuses");

            migrationBuilder.DropIndex(
                name: "IX_shawarmas_kitchen_station_id",
                table: "shawarmas");

            migrationBuilder.DropColumn(
                name: "kitchen_station_id",
                table: "shawarmas");

            migrationBuilder.DropTable(
                name: "kitchen_stations");
        }
    }
}
