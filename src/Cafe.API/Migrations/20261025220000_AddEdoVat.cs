using Microsoft.EntityFrameworkCore.Migrations;
using Cafe.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <summary>НДС по накладной СБИС — для платёжки. Старые накладные досчитываются из XML при открытии списка.</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261025220000_AddEdoVat")]
    public partial class AddEdoVat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "vat_total",
                table: "edo_documents",
                type: "numeric(14,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "without_vat",
                table: "edo_documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "vat_total",
                table: "edo_documents");

            migrationBuilder.DropColumn(
                name: "without_vat",
                table: "edo_documents");
        }
    }
}
