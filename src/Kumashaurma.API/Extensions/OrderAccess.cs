using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Extensions
{
    public static class OrderAccess
    {
        /// <summary>Персонал — к любому заказу, клиент — к своему, гость — по секретному токену заказа</summary>
        public static bool CanAccess(this ClaimsPrincipal user, Order order, string? token)
        {
            if (user.IsStaff()) return true;

            var userId = user.GetUserId();
            if (userId != null && order.UserId == userId) return true;

            return !string.IsNullOrEmpty(token)
                && !string.IsNullOrEmpty(order.AccessToken)
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(order.AccessToken));
        }
    }
}
