using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Cafe.API.Data;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <summary>Возврат счёта «на заведение»: refunded_at, refunded_by</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261109120000_TableCheckRefund")]
    public partial class TableCheckRefund : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE table_checks ADD COLUMN IF NOT EXISTS refunded_at timestamp without time zone;");
            migrationBuilder.Sql("ALTER TABLE table_checks ADD COLUMN IF NOT EXISTS refunded_by integer;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE table_checks DROP COLUMN IF EXISTS refunded_at;");
            migrationBuilder.Sql("ALTER TABLE table_checks DROP COLUMN IF EXISTS refunded_by;");
        }
    }
}
