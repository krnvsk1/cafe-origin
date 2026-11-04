using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Cafe.API.Data;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <summary>Закрытие заказов с сайта менеджером (оплата, на сотрудника) и отмена всего счёта</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261104223000_SiteOrderCloseAndCheckCancel")]
    public partial class SiteOrderCloseAndCheckCancel : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE orders ADD COLUMN IF NOT EXISTS closed_at timestamp without time zone;
ALTER TABLE orders ADD COLUMN IF NOT EXISTS closed_by integer;
ALTER TABLE orders ADD COLUMN IF NOT EXISTS staff_user_id integer;
ALTER TABLE orders ADD COLUMN IF NOT EXISTS staff_reason character varying(60);
ALTER TABLE table_checks ADD COLUMN IF NOT EXISTS cancel_requested_at timestamp without time zone;
ALTER TABLE table_checks ADD COLUMN IF NOT EXISTS cancel_requested_by integer;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE orders DROP COLUMN IF EXISTS closed_at;
ALTER TABLE orders DROP COLUMN IF EXISTS closed_by;
ALTER TABLE orders DROP COLUMN IF EXISTS staff_user_id;
ALTER TABLE orders DROP COLUMN IF EXISTS staff_reason;
ALTER TABLE table_checks DROP COLUMN IF EXISTS cancel_requested_at;
ALTER TABLE table_checks DROP COLUMN IF EXISTS cancel_requested_by;");
        }
    }
}
