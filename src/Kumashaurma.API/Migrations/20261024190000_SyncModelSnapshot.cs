using Kumashaurma.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kumashaurma.API.Migrations
{
    /// <summary>
    /// Выравнивание снапшота модели с кодом.
    /// 1) Уникальность названия позиции — в пределах родителя (как задано в ApplicationDbContext),
    ///    а не глобально: раньше нельзя было завести одинаковый вариант («Большая») в разных карточках.
    /// 2) Снапшот теперь описывает даты как "timestamp without time zone" — так их видит EF при включённом
    ///    Npgsql.EnableLegacyTimestampBehavior. Колонки в базе НЕ меняются: они остаются timestamptz,
    ///    с которыми приложение уже работает. Это исключительно синхронизация снапшота.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261024190000_SyncModelSnapshot")]
    public partial class SyncModelSnapshot : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_shawarmas_name"";");

            migrationBuilder.CreateIndex(
                name: "IX_shawarmas_parent_id_name",
                table: "shawarmas",
                columns: new[] { "parent_id", "name" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_shawarmas_parent_id_name", table: "shawarmas");

            migrationBuilder.CreateIndex(
                name: "IX_shawarmas_name",
                table: "shawarmas",
                column: "name",
                unique: true);
        }
    }
}
