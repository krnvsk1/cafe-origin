using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Cafe.API.Data;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <summary>Счёт, закрытый на сотрудника: кто и почему</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261104190000_TableCheckStaff")]
    public partial class TableCheckStaff : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE table_checks ADD COLUMN IF NOT EXISTS staff_user_id integer;
ALTER TABLE table_checks ADD COLUMN IF NOT EXISTS staff_reason character varying(60);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE table_checks DROP COLUMN IF EXISTS staff_user_id; ALTER TABLE table_checks DROP COLUMN IF EXISTS staff_reason;");
        }
    }
}
