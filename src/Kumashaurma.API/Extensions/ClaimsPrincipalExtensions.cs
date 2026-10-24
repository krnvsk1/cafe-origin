using System.Security.Claims;

namespace Kumashaurma.API.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        /// <summary>ID текущего пользователя из JWT или null</summary>
        public static int? GetUserId(this ClaimsPrincipal user)
        {
            var value = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(value, out var id) ? id : null;
        }

        /// <summary>Сотрудник: admin, manager или courier</summary>
        public static bool IsStaff(this ClaimsPrincipal user) =>
            user.IsInRole("admin") || user.IsInRole("manager") || user.IsInRole("courier");
    }
}
