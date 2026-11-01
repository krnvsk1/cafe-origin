using Cafe.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <summary>Чек на АТОЛ при закрытии счёта официантом: привязка к счёту стола</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261101180000_AddFiscalReceiptCheck")]
    public partial class AddFiscalReceiptCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "check_id",
                table: "fiscal_receipts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_receipts_check_id",
                table: "fiscal_receipts",
                column: "check_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_fiscal_receipts_check_id", table: "fiscal_receipts");
            migrationBuilder.DropColumn(name: "check_id", table: "fiscal_receipts");
        }
    }
}
