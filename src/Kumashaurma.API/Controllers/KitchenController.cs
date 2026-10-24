using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;
using Kumashaurma.API.Services;
using Kumashaurma.API.Authorization;

namespace Kumashaurma.API.Controllers
{
    /// <summary>
    /// Экран кухни: активные заказы с составом
    /// </summary>
    [ApiController]
    [Route("api/kitchen")]
    [HasPermission(Permissions.Kitchen)]
    public class KitchenController : ControllerBase
    {
        // Заказ ко времени появляется в работе за столько минут до срока
        private const int ScheduledLeadMinutes = 60;

        private readonly ApplicationDbContext _context;

        public KitchenController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/kitchen/orders
        [HttpGet("orders")]
        public async Task<IActionResult> GetOrders()
        {
            var active = new[] { OrderStatuses.New, OrderStatuses.Preparing, OrderStatuses.Ready };
            var since = DateTime.UtcNow.AddDays(-2); // защита от «зависших» старых заказов

            var orders = await _context.Orders
                .AsNoTracking()
                .Where(o => active.Contains(o.Status) && o.CreatedAt >= since)
                // Онлайн-заказы — только оплаченные
                .Where(o => o.PaymentMethod != "online" || o.PaymentStatus == "paid")
                .Include(o => o.OrderItems)
                    .ThenInclude(i => i.SelectedAddons)
                .OrderBy(o => o.ScheduledFor ?? o.CreatedAt)
                .ToListAsync();

            var nowUtc = DateTime.UtcNow;
            var result = orders.Select(o =>
            {
                var scheduledUtc = o.ScheduledFor?.ToUniversalTime();
                return new
                {
                    o.Id,
                    o.Status,
                    o.DeliveryType,
                    o.CustomerName,
                    o.Notes,
                    o.Total,
                    o.CreatedAt,
                    o.ScheduledFor,
                    o.PaymentMethod,
                    o.PaymentStatus,
                    // Заказ ко времени, до которого ещё далеко — показывается в колонке «Позже»
                    IsLater = scheduledUtc.HasValue && scheduledUtc.Value > nowUtc.AddMinutes(ScheduledLeadMinutes) && o.Status == OrderStatuses.New,
                    Items = o.OrderItems.Select(i => new
                    {
                        i.Name,
                        i.Quantity,
                        Addons = i.SelectedAddons.Select(a => new { a.AddonName, a.Quantity })
                    })
                };
            });

            return Ok(result);
        }
    }
}
