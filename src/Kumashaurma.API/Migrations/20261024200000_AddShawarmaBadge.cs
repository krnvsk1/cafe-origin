using Kumashaurma.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kumashaurma.API.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261024200000_AddShawarmaBadge")]
    public partial class AddShawarmaBadge : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "badge",
                table: "shawarmas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "badge", table: "shawarmas");
        }
    }
}
