using Cafe.API.DTOs.Auth;
using Cafe.API.Models;

namespace Cafe.API.Authorization
{
    /// <summary>Профиль для фронтенда: роли, их названия и итоговые разрешения</summary>
    public static class UserDtoFactory
    {
        public static async Task<UserDto> CreateAsync(AppUser user, IList<string> roles, IPermissionService permissions)
        {
            var titles = await permissions.GetTitlesAsync();
            var granted = await permissions.GetPermissionsAsync(roles);
            return new UserDto
            {
                Id = user.Id,
                Phone = user.PhoneNumber ?? "",
                FirstName = user.FirstName,
                LastName = user.LastName,
                PhoneVerified = user.PhoneVerified,
                Roles = roles,
                Permissions = granted.OrderBy(p => p).ToList(),
                RoleTitles = roles
                    .Where(r => r != AppRoles.User || roles.Count == 1)
                    .Select(r => titles.TryGetValue(r, out var t) ? t : r)
                    .ToList()
            };
        }
    }
}
