using Kumashaurma.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kumashaurma.API.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261024150000_AddUserTelegram")]
    public partial class AddUserTelegram : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "telegram_user_id",
                table: "users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "telegram_notifications",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_telegram_user_id",
                table: "users",
                column: "telegram_user_id",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_users_telegram_user_id", table: "users");
            migrationBuilder.DropColumn(name: "telegram_user_id", table: "users");
            migrationBuilder.DropColumn(name: "telegram_notifications", table: "users");
        }
    }
}
