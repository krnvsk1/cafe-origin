using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Cafe.API.Authorization;
using Cafe.API.Data;

#nullable disable

namespace Cafe.API.Migrations
{
    /// <summary>
    /// Роль «Техническая поддержка» — все права, не настраивается; её получают все нынешние администраторы.
    /// Администратор становится обычной настраиваемой ролью: пока — со всеми правами, дальше их можно менять.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261106220000_SupportRole")]
    public partial class SupportRole : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var keys = string.Join(", ", Permissions.Keys.Select(k => $"'{k}'"));
            migrationBuilder.Sql($@"
INSERT INTO roles (""Name"", ""NormalizedName"", ""ConcurrencyStamp"")
SELECT 'support', 'SUPPORT', gen_random_uuid()::text
WHERE NOT EXISTS (SELECT 1 FROM roles WHERE ""Name"" = 'support');

INSERT INTO role_claims (""RoleId"", ""ClaimType"", ""ClaimValue"")
SELECT r.""Id"", c.t, c.v FROM roles r
CROSS JOIN (VALUES ('role.title', 'Техническая поддержка'), ('role.description', 'Полный доступ ко всему. Права не настраиваются.')) AS c(t, v)
WHERE r.""Name"" = 'support'
  AND NOT EXISTS (SELECT 1 FROM role_claims x WHERE x.""RoleId"" = r.""Id"" AND x.""ClaimType"" = c.t);

-- Нынешние администраторы — ещё и техподдержка, чтобы никто не потерял полный доступ
INSERT INTO user_roles (""UserId"", ""RoleId"")
SELECT ur.""UserId"", s.""Id""
FROM user_roles ur
JOIN roles a ON a.""Id"" = ur.""RoleId"" AND a.""Name"" = 'admin'
CROSS JOIN roles s
WHERE s.""Name"" = 'support'
ON CONFLICT DO NOTHING;

-- Администратор: все нынешние права явно — дальше их можно менять в «Ролях и доступах»
INSERT INTO role_claims (""RoleId"", ""ClaimType"", ""ClaimValue"")
SELECT a.""Id"", 'permission', p
FROM roles a CROSS JOIN unnest(ARRAY[{keys}]) AS p
WHERE a.""Name"" = 'admin'
  AND NOT EXISTS (SELECT 1 FROM role_claims x WHERE x.""RoleId"" = a.""Id"" AND x.""ClaimType"" = 'permission' AND x.""ClaimValue"" = p);

UPDATE role_claims SET ""ClaimValue"" = 'Управление заведением. Права настраиваются.'
WHERE ""ClaimType"" = 'role.description' AND ""ClaimValue"" LIKE 'Полный доступ%'
  AND ""RoleId"" = (SELECT ""Id"" FROM roles WHERE ""Name"" = 'admin');");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE FROM role_claims WHERE ""RoleId"" = (SELECT ""Id"" FROM roles WHERE ""Name"" = 'admin') AND ""ClaimType"" = 'permission';
DELETE FROM user_roles WHERE ""RoleId"" = (SELECT ""Id"" FROM roles WHERE ""Name"" = 'support');
DELETE FROM role_claims WHERE ""RoleId"" = (SELECT ""Id"" FROM roles WHERE ""Name"" = 'support');
DELETE FROM roles WHERE ""Name"" = 'support';");
        }
    }
}
