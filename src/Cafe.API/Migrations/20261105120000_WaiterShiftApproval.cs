using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Cafe.API.Data;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <summary>
    /// Подтверждение смены официанта менеджером: approved_at/approved_by. Уже открытые смены считаются подтверждёнными.
    /// Роли с правом «Все чеки и смены» получают право «Подтверждение смен официантов».
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261105120000_WaiterShiftApproval")]
    public partial class WaiterShiftApproval : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE waiter_shifts ADD COLUMN IF NOT EXISTS approved_at timestamp without time zone;
ALTER TABLE waiter_shifts ADD COLUMN IF NOT EXISTS approved_by integer;
UPDATE waiter_shifts SET approved_at = opened_at WHERE approved_at IS NULL;
INSERT INTO role_claims (""RoleId"", ""ClaimType"", ""ClaimValue"")
SELECT DISTINCT rc.""RoleId"", 'permission', 'shift.approve' FROM role_claims rc
WHERE rc.""ClaimType"" = 'permission' AND rc.""ClaimValue"" = 'checks.all'
  AND NOT EXISTS (SELECT 1 FROM role_claims x WHERE x.""RoleId"" = rc.""RoleId"" AND x.""ClaimType"" = 'permission' AND x.""ClaimValue"" = 'shift.approve');");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE waiter_shifts DROP COLUMN IF EXISTS approved_at;
ALTER TABLE waiter_shifts DROP COLUMN IF EXISTS approved_by;
DELETE FROM role_claims WHERE ""ClaimType"" = 'permission' AND ""ClaimValue"" = 'shift.approve';");
        }
    }
}
