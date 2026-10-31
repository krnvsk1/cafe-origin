using System;
using Cafe.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <summary>Официант: столы, чеки столов, связь заказа кухни с чеком</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261031180000_AddWaiterChecks")]
    public partial class AddWaiterChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dining_tables",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dining_tables", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "table_checks",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    table_id = table.Column<int>(type: "integer", nullable: false),
                    waiter_id = table.Column<int>(type: "integer", nullable: true),
                    guests = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    opened_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    closed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    total = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_table_checks", x => x.id);
                    table.ForeignKey(
                        name: "FK_table_checks_dining_tables_table_id",
                        column: x => x.table_id,
                        principalTable: "dining_tables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddColumn<int>(
                name: "check_id",
                table: "orders",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_check_id",
                table: "orders",
                column: "check_id");

            migrationBuilder.CreateIndex(
                name: "IX_table_checks_status",
                table: "table_checks",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_table_checks_table_id",
                table: "table_checks",
                column: "table_id");

            migrationBuilder.AddForeignKey(
                name: "FK_orders_table_checks_check_id",
                table: "orders",
                column: "check_id",
                principalTable: "table_checks",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_orders_table_checks_check_id",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_check_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "check_id",
                table: "orders");

            migrationBuilder.DropTable(
                name: "table_checks");

            migrationBuilder.DropTable(
                name: "dining_tables");
        }
    }
}
