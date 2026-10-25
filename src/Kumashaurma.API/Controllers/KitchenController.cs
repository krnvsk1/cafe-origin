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

        // Сколько добавок можно запросить за раз вместе с блюдом
        private const int MaxAddonCards = 20;

        private readonly ApplicationDbContext _context;
        private readonly IInventoryService _inventory;

        public KitchenController(ApplicationDbContext context, IInventoryService inventory)
        {
            _context = context;
            _inventory = inventory;
        }

        // GET: api/kitchen/orders
        [HttpGet("orders")]
        public async Task<IActionResult> GetOrders()
        {
            var active = new[] { OrderStatuses.New, OrderStatuses.Accepted, OrderStatuses.Preparing, OrderStatuses.Ready };
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
                        i.ShawarmaId,
                        i.Name,
                        i.Quantity,
                        Addons = i.SelectedAddons.Select(a => new { a.AddonId, a.AddonName, a.Quantity })
                    })
                };
            });

            return Ok(result);
        }

        // GET: api/kitchen/techcard/{shawarmaId}?addons=1,2 — техкарта блюда (и выбранных добавок) для повара, без себестоимости
        [HttpGet("techcard/{shawarmaId:int}")]
        public async Task<IActionResult> GetTechCard(int shawarmaId, [FromQuery] string? addons)
        {
            var dish = await _context.Shawarmas.AsNoTracking()
                .Where(s => s.Id == shawarmaId)
                .Select(s => new { s.Id, s.Name, s.ParentId, ParentName = s.Parent != null ? s.Parent.Name : null })
                .FirstOrDefaultAsync();
            if (dish == null)
                return NotFound(new { message = "Блюдо не найдено" });

            var addonIds = (addons ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => int.TryParse(x, out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .Take(MaxAddonCards)
                .ToList();
            var addonNames = await _context.Addons.AsNoTracking()
                .Where(a => addonIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Name })
                .ToListAsync();

            var calc = await _inventory.LoadCalculatorAsync();
            var title = dish.ParentName != null && dish.ParentName != dish.Name ? $"{dish.ParentName} · {dish.Name}" : dish.Name;
            // У варианта без своей карты берём карту карточки — как при списании со склада
            var dishCard = KitchenTechCards.ToView(calc.ForDish(dish.Id, dish.ParentId), title, calc);
            var addonCards = addonNames
                .Select(a => KitchenTechCards.ToView(calc.ForAddon(a.Id), a.Name, calc))
                .OfType<KitchenTechCard>()
                .ToList();
            var all = (dishCard != null ? new[] { dishCard } : Array.Empty<KitchenTechCard>()).Concat(addonCards);

            return Ok(new
            {
                title,
                dish = dishCard,
                addons = addonCards,
                semis = KitchenTechCards.SemiCards(all, calc)
            });
        }
    }
}
