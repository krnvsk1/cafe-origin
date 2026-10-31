using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Authorization
{
    /// <summary>Служебные claim'ы роли: название и описание для интерфейса</summary>
    public static class RoleClaimTypes
    {
        public const string Title = "role.title";
        public const string Description = "role.description";
    }

    public interface IPermissionService
    {
        /// <summary>Разрешения набора ролей (admin — все)</summary>
        Task<HashSet<string>> GetPermissionsAsync(IEnumerable<string> roles);

        /// <summary>Названия ролей для интерфейса: ключ → «Менеджер»</summary>
        Task<IReadOnlyDictionary<string, string>> GetTitlesAsync();

        /// <summary>Роли пользователя по базе (null — пользователь не найден)</summary>
        Task<IReadOnlyList<string>?> GetUserRolesAsync(int userId);

        /// <summary>Сбросить кэш после изменения ролей или назначения ролей пользователю</summary>
        void Invalidate();
    }

    /// <summary>
    /// Права ролей хранятся в role_claims (type = permission). Таблица маленькая —
    /// держим её в памяти и перечитываем после изменений.
    /// </summary>
    public class PermissionService : IPermissionService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private Snapshot? _snapshot;

        private record Snapshot(Dictionary<string, HashSet<string>> Permissions, Dictionary<string, string> Titles,
            Dictionary<int, List<string>> UserRoles, DateTime LoadedAt);

        public PermissionService(IServiceScopeFactory scopes) => _scopes = scopes;

        public async Task<HashSet<string>> GetPermissionsAsync(IEnumerable<string> roles)
        {
            var list = roles.ToList();
            if (list.Contains(AppRoles.Admin)) return new HashSet<string>(Permissions.Keys);

            var snapshot = await GetSnapshotAsync();
            var result = new HashSet<string>();
            foreach (var role in list)
                if (snapshot.Permissions.TryGetValue(role, out var perms))
                    result.UnionWith(perms);
            return result;
        }

        public async Task<IReadOnlyDictionary<string, string>> GetTitlesAsync() => (await GetSnapshotAsync()).Titles;

        public async Task<IReadOnlyList<string>?> GetUserRolesAsync(int userId)
        {
            var snapshot = await GetSnapshotAsync();
            if (snapshot.UserRoles.TryGetValue(userId, out var roles)) return roles;
            // Нет в снимке — возможно, пользователь появился позже: перечитаем, но не чаще раза в 10 секунд
            if (DateTime.UtcNow - snapshot.LoadedAt < TimeSpan.FromSeconds(10)) return null;
            Invalidate();
            snapshot = await GetSnapshotAsync();
            return snapshot.UserRoles.TryGetValue(userId, out roles) ? roles : null;
        }

        public void Invalidate() => _snapshot = null;

        private async Task<Snapshot> GetSnapshotAsync()
        {
            var current = _snapshot;
            // На случай правки базы руками — всё равно обновляем раз в 5 минут
            if (current != null && DateTime.UtcNow - current.LoadedAt < TimeSpan.FromMinutes(5)) return current;

            await _lock.WaitAsync();
            try
            {
                if (_snapshot != null && DateTime.UtcNow - _snapshot.LoadedAt < TimeSpan.FromMinutes(5)) return _snapshot;

                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var rows = await db.RoleClaims.AsNoTracking()
                    .Join(db.Roles, c => c.RoleId, r => r.Id, (c, r) => new { Role = r.Name!, c.ClaimType, c.ClaimValue })
                    .ToListAsync();

                var perms = rows.Where(r => r.ClaimType == Permissions.ClaimType && r.ClaimValue != null)
                    .GroupBy(r => r.Role)
                    .ToDictionary(g => g.Key, g => g.Select(r => r.ClaimValue!).ToHashSet());
                var titles = rows.Where(r => r.ClaimType == RoleClaimTypes.Title && !string.IsNullOrEmpty(r.ClaimValue))
                    .GroupBy(r => r.Role)
                    .ToDictionary(g => g.Key, g => g.First().ClaimValue!);

                // Кто какие роли носит — по базе, а не по токену: назначенная роль действует сразу, без перевхода
                var userRoles = (await db.UserRoles.AsNoTracking()
                        .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, Role = r.Name! })
                        .ToListAsync())
                    .GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.Role).ToList());

                _snapshot = new Snapshot(perms, titles, userRoles, DateTime.UtcNow);
                return _snapshot;
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    /// <summary>
    /// На каждый запрос подставляет пользователю его роли по базе и claim'ы permission по ним.
    /// Так и изменения прав роли, и назначение роли действуют сразу, без перевыпуска токенов.
    /// </summary>
    public class PermissionClaimsTransformation : IClaimsTransformation
    {
        private readonly IPermissionService _permissions;

        public PermissionClaimsTransformation(IPermissionService permissions) => _permissions = permissions;

        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            if (principal.Identity?.IsAuthenticated != true || principal.HasClaim(c => c.Type == Permissions.ClaimType))
                return principal;

            var tokenRoles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct().ToList();
            var idText = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
            var dbRoles = int.TryParse(idText, out var userId) ? await _permissions.GetUserRolesAsync(userId) : null;
            var roles = dbRoles?.ToList() ?? tokenRoles;

            // Роли в токене устарели — собираем пользователя заново с ролями из базы
            if (dbRoles != null && (roles.Count != tokenRoles.Count || roles.Except(tokenRoles).Any()) && principal.Identity is ClaimsIdentity source)
            {
                var claims = source.Claims.Where(c => c.Type != ClaimTypes.Role && c.Type != "role" && c.Type != source.RoleClaimType).ToList();
                claims.AddRange(roles.Select(r => new Claim(source.RoleClaimType, r)));
                if (source.RoleClaimType != ClaimTypes.Role) claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
                principal = new ClaimsPrincipal(new ClaimsIdentity(claims, source.AuthenticationType, source.NameClaimType, source.RoleClaimType));
            }
            if (roles.Count == 0) return principal;

            var granted = await _permissions.GetPermissionsAsync(roles);
            if (granted.Count == 0) return principal;

            principal.AddIdentity(new ClaimsIdentity(granted.Select(p => new Claim(Permissions.ClaimType, p))));
            return principal;
        }
    }

    public static class PermissionPrincipalExtensions
    {
        public static bool HasPermission(this ClaimsPrincipal user, string permission) =>
            user.IsInRole(AppRoles.Admin) || user.HasClaim(Permissions.ClaimType, permission);

        public static bool HasAnyPermission(this ClaimsPrincipal user, params string[] permissions) =>
            permissions.Any(user.HasPermission);
    }

    /// <summary>
    /// [HasPermission(Permissions.Menu)] — доступ, если у пользователя есть хотя бы одно из перечисленных разрешений.
    /// Несколько атрибутов на классе и методе действуют вместе (И).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HasPermissionAttribute : AuthorizeAttribute
    {
        public const string Prefix = "perm:";

        public HasPermissionAttribute(params string[] permissions) : base(Prefix + string.Join('|', permissions)) { }
    }

    public class PermissionRequirement : IAuthorizationRequirement
    {
        public string[] Any { get; }
        public PermissionRequirement(string[] any) => Any = any;
    }

    public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (context.User.HasAnyPermission(requirement.Any))
                context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }

    /// <summary>Политики вида «perm:menu|stoplist» создаются на лету</summary>
    public class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
    {
        public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options) { }

        public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            if (!policyName.StartsWith(HasPermissionAttribute.Prefix))
                return await base.GetPolicyAsync(policyName);

            var keys = policyName[HasPermissionAttribute.Prefix.Length..].Split('|', StringSplitOptions.RemoveEmptyEntries);
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(keys))
                .Build();
        }
    }

    /// <summary>Встроенные роли и их права по умолчанию — при первом запуске</summary>
    public static class RoleSeeder
    {
        public static readonly IReadOnlyDictionary<string, (string Title, string Description)> BuiltIn =
            new Dictionary<string, (string, string)>
            {
                [AppRoles.Admin] = ("Администратор", "Полный доступ ко всему. Права не настраиваются."),
                [AppRoles.Manager] = ("Менеджер", "Управление заказами, меню и маркетингом"),
                [AppRoles.Courier] = ("Курьер", "Доставка заказов"),
                [AppRoles.User] = ("Клиент", "Обычный покупатель — есть у всех зарегистрированных"),
            };

        public static async Task SeedAsync(RoleManager<IdentityRole<int>> roleManager)
        {
            foreach (var (name, (title, description)) in BuiltIn)
            {
                var role = await roleManager.FindByNameAsync(name);
                if (role == null) continue;

                var claims = await roleManager.GetClaimsAsync(role);
                // Уже настроено (есть название) — не трогаем: права мог изменить админ
                if (claims.Any(c => c.Type == RoleClaimTypes.Title)) continue;

                await roleManager.AddClaimAsync(role, new Claim(RoleClaimTypes.Title, title));
                await roleManager.AddClaimAsync(role, new Claim(RoleClaimTypes.Description, description));
                if (Permissions.Defaults.TryGetValue(name, out var defaults))
                    foreach (var p in defaults)
                        await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, p));
            }
        }
    }
}
