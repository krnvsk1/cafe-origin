using System.Security.Claims;
using Cafe.API.Authorization;

namespace Cafe.API.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        /// <summary>ID текущего пользователя из JWT или null</summary>
        public static int? GetUserId(this ClaimsPrincipal user)
        {
            var value = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(value, out var id) ? id : null;
        }

        /// <summary>Сотрудник, которому видны все заказы: «Все заказы», «Касса», «Кухня» или «Доставка»</summary>
        public static bool IsStaff(this ClaimsPrincipal user) =>
            user.HasAnyPermission(Permissions.OrdersView, Permissions.Kitchen, Permissions.Delivery, Permissions.Pos, Permissions.Waiter);
    }
}
