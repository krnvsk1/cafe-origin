using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSortOrderToShawarma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "sort_order",
                table: "shawarmas",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sort_order",
                table: "shawarmas");
        }
    }
}
