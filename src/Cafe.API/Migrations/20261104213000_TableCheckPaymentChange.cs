using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Cafe.API.Data;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <summary>Смена способа оплаты закрытого счёта: какой был, кто и когда поменял</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261104213000_TableCheckPaymentChange")]
    public partial class TableCheckPaymentChange : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE table_checks ADD COLUMN IF NOT EXISTS payment_was character varying(20);
ALTER TABLE table_checks ADD COLUMN IF NOT EXISTS payment_changed_by integer;
ALTER TABLE table_checks ADD COLUMN IF NOT EXISTS payment_changed_at timestamp without time zone;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE table_checks DROP COLUMN IF EXISTS payment_was;
ALTER TABLE table_checks DROP COLUMN IF EXISTS payment_changed_by;
ALTER TABLE table_checks DROP COLUMN IF EXISTS payment_changed_at;");
        }
    }
}
