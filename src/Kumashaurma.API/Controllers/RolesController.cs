using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Authorization;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Controllers
{
    /// <summary>
    /// Роли и их права. Администратор может всё и не настраивается; «Клиент» — без доступа к управлению.
    /// Не-администратор с правом «Роли и доступы» может выдавать только те права, что есть у него самого.
    /// </summary>
    [ApiController]
    [Route("api/roles")]
    [Authorize]
    public class RolesController : ControllerBase
    {
        private readonly RoleManager<IdentityRole<int>> _roleManager;
        private readonly ApplicationDbContext _context;
        private readonly IPermissionService _permissions;

        public RolesController(RoleManager<IdentityRole<int>> roleManager, ApplicationDbContext context, IPermissionService permissions)
        {
            _roleManager = roleManager;
            _context = context;
            _permissions = permissions;
        }

        public class RoleRequest
        {
            public string Title { get; set; } = string.Empty;
            public string? Description { get; set; }
            public List<string> Permissions { get; set; } = new();
        }

        // GET: api/roles/permissions — каталог разрешений по группам
        [HttpGet("permissions")]
        [HasPermission(Permissions.Roles, Permissions.UsersRoles, Permissions.UsersView)]
        public IActionResult GetCatalog() =>
            Ok(Permissions.All
                .GroupBy(p => p.Group)
                .Select(g => new { group = g.Key, permissions = g.Select(p => new { p.Key, p.Title, p.Description }) }));

        // GET: api/roles — роли с правами и числом пользователей
        [HttpGet]
        [HasPermission(Permissions.Roles, Permissions.UsersRoles, Permissions.UsersView)]
        public async Task<IActionResult> GetRoles()
        {
            var roles = await _context.Roles.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
            var claims = await _context.RoleClaims.AsNoTracking().ToListAsync();
            var counts = await _context.UserRoles.AsNoTracking()
                .GroupBy(ur => ur.RoleId)
                .Select(g => new { RoleId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RoleId, x => x.Count);

            var result = roles.Select(r =>
            {
                var own = claims.Where(c => c.RoleId == r.Id).ToList();
                var isAdmin = r.Name == AppRoles.Admin;
                return new
                {
                    name = r.Name,
                    title = own.FirstOrDefault(c => c.ClaimType == RoleClaimTypes.Title)?.ClaimValue ?? r.Name,
                    description = own.FirstOrDefault(c => c.ClaimType == RoleClaimTypes.Description)?.ClaimValue,
                    isSystem = AppRoles.AllRoles.Contains(r.Name),
                    // Права админа и клиента не настраиваются
                    isLocked = isAdmin || r.Name == AppRoles.User,
                    permissions = isAdmin
                        ? Permissions.All.Select(p => p.Key).ToList()
                        : own.Where(c => c.ClaimType == Permissions.ClaimType && c.ClaimValue != null)
                             .Select(c => c.ClaimValue!).Where(Permissions.Keys.Contains).Distinct().ToList(),
                    usersCount = counts.GetValueOrDefault(r.Id)
                };
            })
            // Сначала служебные роли, потом созданные админом
            .OrderBy(r => r.name == AppRoles.Admin ? 0 : r.isSystem && r.name != AppRoles.User ? 1 : r.name == AppRoles.User ? 3 : 2)
            .ThenBy(r => r.title);

            return Ok(result);
        }

        // POST: api/roles — новая роль
        [HttpPost]
        [HasPermission(Permissions.Roles)]
        public async Task<IActionResult> Create([FromBody] RoleRequest request)
        {
            var error = await ValidateAsync(request, null);
            if (error != null) return BadRequest(new { message = error });

            var name = await UniqueNameAsync(request.Title);
            var role = new IdentityRole<int>(name);
            var created = await _roleManager.CreateAsync(role);
            if (!created.Succeeded)
                return BadRequest(new { message = string.Join(", ", created.Errors.Select(e => e.Description)) });

            await WriteClaimsAsync(role, request, permissionsLocked: false);
            _permissions.Invalidate();
            return Ok(new { name });
        }

        // PUT: api/roles/{name}
        [HttpPut("{name}")]
        [HasPermission(Permissions.Roles)]
        public async Task<IActionResult> Update(string name, [FromBody] RoleRequest request)
        {
            var role = await _roleManager.FindByNameAsync(name);
            if (role == null) return NotFound(new { message = "Роль не найдена" });

            var locked = name == AppRoles.Admin || name == AppRoles.User;
            var error = await ValidateAsync(request, role, permissionsLocked: locked);
            if (error != null) return BadRequest(new { message = error });

            // Не-админ не может менять роль, в которой есть права сверх его собственных
            if (!User.IsInRole(AppRoles.Admin))
            {
                if (name == AppRoles.Admin) return Forbid();
                var current = await _permissions.GetPermissionsAsync(new[] { name });
                var mine = await MyPermissionsAsync();
                if (!current.IsSubsetOf(mine))
                    return BadRequest(new { message = "У этой роли есть права, которых нет у вас, — изменить её может только администратор" });
            }

            await WriteClaimsAsync(role, request, permissionsLocked: locked);
            _permissions.Invalidate();
            return Ok(new { name });
        }

        // DELETE: api/roles/{name} — только созданные админом роли
        [HttpDelete("{name}")]
        [HasPermission(Permissions.Roles)]
        public async Task<IActionResult> Delete(string name)
        {
            if (AppRoles.AllRoles.Contains(name))
                return BadRequest(new { message = "Встроенную роль удалить нельзя" });

            var role = await _roleManager.FindByNameAsync(name);
            if (role == null) return NotFound(new { message = "Роль не найдена" });

            if (!User.IsInRole(AppRoles.Admin))
            {
                var current = await _permissions.GetPermissionsAsync(new[] { name });
                if (!current.IsSubsetOf(await MyPermissionsAsync()))
                    return BadRequest(new { message = "Удалить эту роль может только администратор" });
            }

            // Пользователи просто теряют эту роль (user_roles удаляются каскадом)
            var result = await _roleManager.DeleteAsync(role);
            if (!result.Succeeded)
                return BadRequest(new { message = string.Join(", ", result.Errors.Select(e => e.Description)) });

            _permissions.Invalidate();
            return NoContent();
        }

        // ================= Вспомогательное =================

        private async Task<string?> ValidateAsync(RoleRequest r, IdentityRole<int>? existing, bool permissionsLocked = false)
        {
            if (string.IsNullOrWhiteSpace(r.Title)) return "Введите название роли";
            if (r.Title.Trim().Length > 60) return "Название — не длиннее 60 символов";
            if ((r.Description?.Length ?? 0) > 300) return "Описание — не длиннее 300 символов";

            var unknown = r.Permissions.FirstOrDefault(p => !Permissions.Keys.Contains(p));
            if (unknown != null) return $"Неизвестное разрешение: {unknown}";

            // Название должно быть уникальным среди ролей
            var title = r.Title.Trim();
            var duplicate = await _context.RoleClaims.AsNoTracking()
                .AnyAsync(c => c.ClaimType == RoleClaimTypes.Title && c.ClaimValue == title && (existing == null || c.RoleId != existing.Id));
            if (duplicate) return "Роль с таким названием уже есть";

            if (!permissionsLocked && !User.IsInRole(AppRoles.Admin))
            {
                var mine = await MyPermissionsAsync();
                var extra = r.Permissions.FirstOrDefault(p => !mine.Contains(p));
                if (extra != null)
                {
                    var t = Permissions.All.First(p => p.Key == extra).Title;
                    return $"Нельзя выдать право «{t}» — у вас его нет";
                }
            }
            return null;
        }

        private async Task WriteClaimsAsync(IdentityRole<int> role, RoleRequest r, bool permissionsLocked)
        {
            var existing = await _context.RoleClaims.Where(c => c.RoleId == role.Id).ToListAsync();
            var toRemove = existing.Where(c =>
                c.ClaimType == RoleClaimTypes.Title || c.ClaimType == RoleClaimTypes.Description
                || (!permissionsLocked && c.ClaimType == Permissions.ClaimType));
            _context.RoleClaims.RemoveRange(toRemove);

            _context.RoleClaims.Add(new IdentityRoleClaim<int> { RoleId = role.Id, ClaimType = RoleClaimTypes.Title, ClaimValue = r.Title.Trim() });
            if (!string.IsNullOrWhiteSpace(r.Description))
                _context.RoleClaims.Add(new IdentityRoleClaim<int> { RoleId = role.Id, ClaimType = RoleClaimTypes.Description, ClaimValue = r.Description.Trim() });

            if (!permissionsLocked)
                foreach (var p in r.Permissions.Distinct())
                    _context.RoleClaims.Add(new IdentityRoleClaim<int> { RoleId = role.Id, ClaimType = Permissions.ClaimType, ClaimValue = p });

            await _context.SaveChangesAsync();
        }

        private Task<HashSet<string>> MyPermissionsAsync() =>
            _permissions.GetPermissionsAsync(User.FindAll(ClaimTypes.Role).Select(c => c.Value));

        /// <summary>Техническое имя роли из названия: «Старший повар» → starshiy-povar</summary>
        private async Task<string> UniqueNameAsync(string title)
        {
            var map = new Dictionary<char, string>
            {
                ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e", ['ж'] = "zh",
                ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o",
                ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "ts",
                ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya"
            };
            var sb = new StringBuilder();
            foreach (var ch in title.Trim().ToLowerInvariant())
            {
                if (map.TryGetValue(ch, out var lat)) sb.Append(lat);
                else if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
                else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            }
            var baseName = sb.ToString().Trim('-');
            if (baseName.Length == 0) baseName = "role";
            if (baseName.Length > 40) baseName = baseName[..40].Trim('-');

            var name = baseName;
            for (var i = 2; await _roleManager.RoleExistsAsync(name) || AppRoles.AllRoles.Contains(name); i++)
                name = $"{baseName}-{i}";
            return name;
        }
    }
}
