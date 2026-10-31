using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;
using Kumashaurma.API.Services;
using Kumashaurma.API.Authorization;
using Kumashaurma.API.Extensions;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Controllers
{
    /// <summary>
    /// Экран кухни: активные заказы с составом
    /// </summary>
    [ApiController]
    [Route("api/kitchen")]
    [HasPermission(Permissions.Kitchen, Permissions.Menu)]
    public class KitchenController : ControllerBase
    {
        // Заказ ко времени появляется в работе за столько минут до срока
        private const int ScheduledLeadMinutes = 60;

        // Сколько добавок можно запросить за раз вместе с блюдом
        private const int MaxAddonCards = 20;

        private readonly ApplicationDbContext _context;
        private readonly IInventoryService _inventory;
        private readonly IOrderStatusService _statusService;
        private readonly IOrderEventBus _events;

        private static readonly string[] DefaultStations = { "Горячая кухня", "Мангал" };

        public KitchenController(ApplicationDbContext context, IInventoryService inventory, IOrderStatusService statusService, IOrderEventBus events)
        {
            _context = context;
            _inventory = inventory;
            _statusService = statusService;
            _events = events;
        }

        public class RenameRequest
        {
            public string Name { get; set; } = string.Empty;
        }

        public class StationStatusRequest
        {
            public string Status { get; set; } = string.Empty;
        }

        /// <summary>Кухни по порядку; при первом обращении заводятся «Горячая кухня» и «Мангал»</summary>
        private async Task<List<KitchenStation>> StationsAsync()
        {
            var list = await _context.KitchenStations.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
            if (list.Count > 0) return list;
            for (var i = 0; i < DefaultStations.Length; i++)
                _context.KitchenStations.Add(new KitchenStation { Name = DefaultStations[i], SortOrder = i });
            await _context.SaveChangesAsync();
            return await _context.KitchenStations.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
        }

        // GET: api/kitchen/stations
        [HttpGet("stations")]
        public async Task<IActionResult> GetStations() =>
            Ok((await StationsAsync()).Select(s => new { s.Id, s.Name }));

        // PUT: api/kitchen/stations/{id} — переименовать кухню (только администратор)
        [HttpPut("stations/{id:int}")]
        public async Task<IActionResult> RenameStation(int id, [FromBody] RenameRequest r)
        {
            if (!User.IsInRole(AppRoles.Admin)) return StatusCode(403, new { message = "Переименовать кухню может только администратор" });
            var name = (r.Name ?? "").Trim();
            if (name.Length == 0 || name.Length > 60) return BadRequest(new { message = "Название кухни — от 1 до 60 символов" });
            var st = await _context.KitchenStations.FirstOrDefaultAsync(s => s.Id == id);
            if (st == null) return NotFound(new { message = "Кухня не найдена" });
            if (await _context.KitchenStations.AnyAsync(s => s.Id != id && s.Name.ToLower() == name.ToLower()))
                return BadRequest(new { message = "Кухня с таким названием уже есть" });
            st.Name = name;
            await _context.SaveChangesAsync();
            return Ok(new { st.Id, st.Name });
        }

        // POST: api/kitchen/orders/{id}/stations/{stationId} — кухня отмечает свою часть заказа
        [HttpPost("orders/{id:int}/stations/{stationId:int}")]
        [HasPermission(Permissions.Kitchen)]
        public async Task<IActionResult> SetStationStatus(int id, int stationId, [FromBody] StationStatusRequest r)
        {
            if (r.Status is not (OrderStatuses.Accepted or OrderStatuses.Preparing or OrderStatuses.Ready))
                return BadRequest(new { message = "Статус кухни: Принят, Готовится или Готов" });
            var order = await _context.Orders.Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) return NotFound(new { message = "Заказ не найден" });
            if (KitchenFlow.Rank(order.Status) < 0) return BadRequest(new { message = $"Заказ {order.Status.ToLower()}" });

            var stations = await StationsAsync();
            var itemStations = await ItemStationsAsync(order.OrderItems.Select(i => i.ShawarmaId), stations[0].Id);
            var involved = order.OrderItems.Select(i => itemStations.GetValueOrDefault(i.ShawarmaId, stations[0].Id)).Distinct().ToList();
            if (!involved.Contains(stationId)) return BadRequest(new { message = "В заказе нет блюд для этой кухни" });

            var rows = await _context.OrderStationStatuses.Where(x => x.OrderId == id).ToListAsync();
            var row = rows.FirstOrDefault(x => x.StationId == stationId);
            if (row == null)
            {
                row = new OrderStationStatus { OrderId = id, StationId = stationId };
                _context.OrderStationStatuses.Add(row);
                rows.Add(row);
            }
            row.Status = r.Status;
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedByUserId = User.GetUserId();
            await _context.SaveChangesAsync();

            var effective = involved.Select(sid => KitchenFlow.Effective(rows.FirstOrDefault(x => x.StationId == sid)?.Status, order.Status)).ToList();
            var overall = KitchenFlow.Overall(effective, order.Status);
            if (overall != order.Status)
                await _statusService.ChangeStatusAsync(order, overall);
            else
                _events.Publish(new OrderEvent("updated", order.Id, order.UserId, order.Status, null, DateTime.UtcNow));
            return Ok(new { order.Id, order.Status });
        }

        /// <summary>Кухня каждого блюда: своя метка, иначе метка карточки, иначе кухня по умолчанию</summary>
        private async Task<Dictionary<int, int>> ItemStationsAsync(IEnumerable<int> shawarmaIds, int defaultStation)
        {
            var ids = shawarmaIds.Distinct().ToList();
            var rows = await _context.Shawarmas.AsNoTracking().Where(s => ids.Contains(s.Id))
                .Select(s => new { s.Id, Own = s.KitchenStationId, Parent = s.Parent != null ? s.Parent.KitchenStationId : null })
                .ToListAsync();
            return rows.ToDictionary(x => x.Id, x => KitchenFlow.StationFor(x.Own, x.Parent, defaultStation));
        }

        // GET: api/kitchen/orders
        [HttpGet("orders")]
        [HasPermission(Permissions.Kitchen)]
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

            var stations = await StationsAsync();
            var names = stations.ToDictionary(s => s.Id, s => s.Name);
            var defaultStation = stations[0].Id;
            var itemStations = await ItemStationsAsync(orders.SelectMany(o => o.OrderItems).Select(i => i.ShawarmaId), defaultStation);
            var orderIds = orders.Select(o => o.Id).ToList();
            var marks = (await _context.OrderStationStatuses.AsNoTracking().Where(x => orderIds.Contains(x.OrderId)).ToListAsync())
                .ToDictionary(x => (x.OrderId, x.StationId), x => x.Status);
            int StationOf(int shawarmaId) => itemStations.GetValueOrDefault(shawarmaId, defaultStation);

            var nowUtc = DateTime.UtcNow;
            var result = orders.Select(o =>
            {
                var scheduledUtc = o.ScheduledFor?.ToUniversalTime();
                var orderStations = o.OrderItems.Select(i => StationOf(i.ShawarmaId)).Distinct()
                    .OrderBy(id => stations.FindIndex(s => s.Id == id))
                    .Select(id => new
                    {
                        Id = id,
                        Name = names.GetValueOrDefault(id, "Кухня"),
                        Status = KitchenFlow.Effective(marks.GetValueOrDefault((o.Id, id)), o.Status)
                    }).ToList();
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
                    Stations = orderStations,
                    Items = o.OrderItems.Select(i => new
                    {
                        i.ShawarmaId,
                        StationId = StationOf(i.ShawarmaId),
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
        [HasPermission(Permissions.Kitchen)]
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
