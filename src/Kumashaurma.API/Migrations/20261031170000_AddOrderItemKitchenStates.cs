using System;
using Kumashaurma.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kumashaurma.API.Migrations
{
    /// <summary>Готовность и выдача каждого блюда заказа: блюда отдаются по мере готовности</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261031170000_AddOrderItemKitchenStates")]
    public partial class AddOrderItemKitchenStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_item_kitchen_states",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_item_id = table.Column<int>(type: "integer", nullable: false),
                    ready_quantity = table.Column<int>(type: "integer", nullable: false),
                    served_quantity = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_item_kitchen_states", x => x.id);
                    table.ForeignKey(
                        name: "FK_order_item_kitchen_states_order_items_order_item_id",
                        column: x => x.order_item_id,
                        principalTable: "order_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_item_kitchen_states_order_item_id",
                table: "order_item_kitchen_states",
                column: "order_item_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_item_kitchen_states");
        }
    }
}
