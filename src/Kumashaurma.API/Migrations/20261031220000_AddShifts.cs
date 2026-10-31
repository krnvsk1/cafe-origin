using System;
using Kumashaurma.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kumashaurma.API.Migrations
{
    /// <summary>Смены заведения и официантов; счёт помнит, в какую смену открыт</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261031220000_AddShifts")]
    public partial class AddShifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "venue_shift_id",
                table: "table_checks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "waiter_shift_id",
                table: "table_checks",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "venue_shifts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    opened_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    opened_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    closed_by_user_id = table.Column<int>(type: "integer", nullable: true),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_venue_shifts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "waiter_shifts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    venue_shift_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    opened_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    closed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_waiter_shifts", x => x.id);
                    table.ForeignKey(
                        name: "FK_waiter_shifts_venue_shifts_venue_shift_id",
                        column: x => x.venue_shift_id,
                        principalTable: "venue_shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_table_checks_venue_shift_id",
                table: "table_checks",
                column: "venue_shift_id");

            migrationBuilder.CreateIndex(
                name: "IX_table_checks_waiter_shift_id",
                table: "table_checks",
                column: "waiter_shift_id");

            migrationBuilder.CreateIndex(
                name: "IX_venue_shifts_business_date",
                table: "venue_shifts",
                column: "business_date");

            migrationBuilder.CreateIndex(
                name: "IX_venue_shifts_closed_at",
                table: "venue_shifts",
                column: "closed_at");

            migrationBuilder.CreateIndex(
                name: "IX_waiter_shifts_user_id",
                table: "waiter_shifts",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_waiter_shifts_venue_shift_id",
                table: "waiter_shifts",
                column: "venue_shift_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "waiter_shifts");

            migrationBuilder.DropTable(
                name: "venue_shifts");

            migrationBuilder.DropIndex(
                name: "IX_table_checks_venue_shift_id",
                table: "table_checks");

            migrationBuilder.DropIndex(
                name: "IX_table_checks_waiter_shift_id",
                table: "table_checks");

            migrationBuilder.DropColumn(
                name: "venue_shift_id",
                table: "table_checks");

            migrationBuilder.DropColumn(
                name: "waiter_shift_id",
                table: "table_checks");
        }
    }
}
