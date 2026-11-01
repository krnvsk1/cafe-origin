using System;
using Cafe.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <summary>Печать счёта для гостя: очередь заданий для принтера и отметка о печати в счёте</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261101170000_AddPrintJobs")]
    public partial class AddPrintJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "print_jobs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    check_id = table.Column<int>(type: "integer", nullable: true),
                    title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    taken_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    done_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_print_jobs", x => x.id);
                });

            migrationBuilder.CreateIndex(name: "IX_print_jobs_check_id", table: "print_jobs", column: "check_id");
            migrationBuilder.CreateIndex(name: "IX_print_jobs_status", table: "print_jobs", column: "status");

            migrationBuilder.AddColumn<DateTime>(
                name: "printed_at",
                table: "table_checks",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "printed_total",
                table: "table_checks",
                type: "numeric(12,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "printed_total", table: "table_checks");
            migrationBuilder.DropColumn(name: "printed_at", table: "table_checks");
            migrationBuilder.DropTable(name: "print_jobs");
        }
    }
}
