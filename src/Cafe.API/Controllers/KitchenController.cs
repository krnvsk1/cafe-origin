using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Services;
using Cafe.API.Authorization;
using Cafe.API.Extensions;
using Cafe.API.Models;

namespace Cafe.API.Controllers
{
    /// <summary>
    /// Экран кухни: активные заказы с составом
    /// </summary>
    [ApiController]
    [Route("api/kitchen")]
    [HasPermission(Permissions.Kitchen, Permissions.Menu, Permissions.Waiter, Permissions.ChecksAll)]
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
        private readonly WaiterReadyNotifier _notifier;

        private static readonly string[] DefaultStations = { "Горячая кухня", "Мангал" };

        public KitchenController(ApplicationDbContext context, IInventoryService inventory, IOrderStatusService statusService, IOrderEventBus events, WaiterReadyNotifier notifier)
        {
            _notifier = notifier;
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

        public class ItemCountRequest
        {
            /// <summary>На сколько штук изменить: +1 — ещё одна готова/отдана, −1 — отменить отметку; null — все</summary>
            public int? Delta { get; set; }
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

        public class WeightRequest
        {
            public int Grams { get; set; }
        }

        // PUT: api/kitchen/items/{id}/weight — поправить вес весового блюда (повар взвесил, официант уточнил).
        // Сумма заказа и счёта пересчитывается; если продукты уже списаны — дописывается или возвращается разница.
        [HttpPut("items/{id:int}/weight")]
        [HasPermission(Permissions.Kitchen, Permissions.Waiter, Permissions.ChecksAll)]
        public async Task<IActionResult> SetWeight(int id, [FromBody] WeightRequest r)
        {
            var item = await _context.OrderItems.Include(i => i.SelectedAddons).Include(i => i.Order).FirstOrDefaultAsync(i => i.Id == id);
            if (item?.Order == null) return NotFound(new { message = "Позиция не найдена" });
            var order = item.Order;
            if (item.Weight is not > 0) return BadRequest(new { message = "Это блюдо продаётся поштучно" });
            if (Weighing.Validate(true, r.Grams, Weighing.StripWeight(item.Name)) is { } error) return BadRequest(new { message = error });
            if (OrderStatuses.IsFinal(order.Status)) return BadRequest(new { message = "Заказ уже закрыт" });
            if (order.PaymentStatus == "paid") return BadRequest(new { message = "Заказ уже оплачен — вес не изменить" });
            var checkId = item.CheckId ?? order.CheckId;
            if (checkId != null && await _context.TableChecks.AnyAsync(c => c.Id == checkId && c.Status != CheckStatuses.Open))
                return BadRequest(new { message = "Счёт уже закрыт" });

            var oldGrams = item.Weight.Value;
            if (oldGrams == r.Grams) return Ok(new { item.Id, item.Weight, item.Price, item.Name });
            // Цена за 100 г — та, по которой блюдо заказали (меню могло с тех пор поменяться)
            var per100 = item.Price * 100m / oldGrams;
            var oldPrice = item.Price;
            item.Price = Weighing.PriceFor(per100, r.Grams);
            item.Weight = r.Grams;
            item.Name = Weighing.NameWith(Weighing.StripWeight(item.Name), r.Grams);
            order.Total += (item.Price - oldPrice) * item.Quantity;
            await _inventory.AdjustWeightStockAsync(order, item, oldGrams, r.Grams);
            await _context.SaveChangesAsync();

            _events.Publish(new OrderEvent("updated", order.Id, order.UserId, order.Status, null, DateTime.UtcNow));
            return Ok(new { item.Id, item.Weight, item.Price, item.Name });
        }

        // GET: api/kitchen/stations
        [HttpGet("stations")]
        public async Task<IActionResult> GetStations() =>
            Ok((await StationsAsync()).Select(s => new { s.Id, s.Name }));

        // PUT: api/kitchen/stations/{id} — переименовать кухню (только администратор)
        [HttpPut("stations/{id:int}")]
        public async Task<IActionResult> RenameStation(int id, [FromBody] RenameRequest r)
        {
            if (AdminOnly() is { } deny) return deny;
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

        public class StationOrderRequest
        {
            public List<int> Ids { get; set; } = new();
        }

        private IActionResult? AdminOnly() =>
            User.IsInRole(AppRoles.Admin) ? null : StatusCode(403, new { message = "Кухни настраивает только администратор" });

        // POST: api/kitchen/stations — добавить кухню (только администратор)
        [HttpPost("stations")]
        public async Task<IActionResult> AddStation([FromBody] RenameRequest r)
        {
            if (AdminOnly() is { } deny) return deny;
            var name = (r.Name ?? "").Trim();
            if (name.Length == 0 || name.Length > 60) return BadRequest(new { message = "Название кухни — от 1 до 60 символов" });
            var list = await StationsAsync();
            if (list.Count >= 20) return BadRequest(new { message = "Кухонь не больше 20" });
            if (list.Any(s => s.Name.ToLower() == name.ToLower())) return BadRequest(new { message = "Кухня с таким названием уже есть" });
            var st = new KitchenStation { Name = name, SortOrder = list.Max(s => s.SortOrder) + 1 };
            _context.KitchenStations.Add(st);
            await _context.SaveChangesAsync();
            return Ok(new { st.Id, st.Name });
        }

        // PUT: api/kitchen/stations/order — порядок кухонь; первая — для блюд без метки
        [HttpPut("stations/order")]
        public async Task<IActionResult> OrderStations([FromBody] StationOrderRequest r)
        {
            if (AdminOnly() is { } deny) return deny;
            var all = await _context.KitchenStations.ToListAsync();
            if (r.Ids.Count != all.Count || r.Ids.Distinct().Count() != all.Count || all.Any(s => !r.Ids.Contains(s.Id)))
                return BadRequest(new { message = "Передайте все кухни по одному разу" });
            for (var i = 0; i < r.Ids.Count; i++) all.First(s => s.Id == r.Ids[i]).SortOrder = i;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/kitchen/stations/{id} — убрать кухню: её блюда уходят на первую кухню
        [HttpDelete("stations/{id:int}")]
        public async Task<IActionResult> DeleteStation(int id)
        {
            if (AdminOnly() is { } deny) return deny;
            var list = await StationsAsync();
            var st = list.FirstOrDefault(s => s.Id == id);
            if (st == null) return NotFound(new { message = "Кухня не найдена" });
            if (list.Count == 1) return BadRequest(new { message = "Нельзя убрать единственную кухню" });

            // Не убираем кухню, пока на ней готовятся заказы
            var active = new[] { OrderStatuses.New, OrderStatuses.Accepted, OrderStatuses.Preparing, OrderStatuses.Ready };
            var activeItems = await _context.Orders.AsNoTracking().Where(o => active.Contains(o.Status))
                .SelectMany(o => o.OrderItems.Select(i => i.DishId)).Distinct().ToListAsync();
            var stationOf = await ItemStationsAsync(activeItems, list[0].Id);
            if (activeItems.Any(sid => stationOf.GetValueOrDefault(sid, list[0].Id) == id))
                return BadRequest(new { message = "На этой кухне сейчас есть заказы — уберите её, когда они будут готовы" });

            var dishes = await _context.Dishes.Where(s => s.KitchenStationId == id).ToListAsync();
            foreach (var d in dishes) d.KitchenStationId = null;
            _context.KitchenStations.Remove(await _context.KitchenStations.FirstAsync(s => s.Id == id));
            await _context.SaveChangesAsync();
            return Ok(new { movedDishes = dishes.Count });
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
            var itemStations = await ItemStationsAsync(order.OrderItems.Select(i => i.DishId), stations[0].Id);
            var involved = order.OrderItems.Select(i => itemStations.GetValueOrDefault(i.DishId, stations[0].Id)).Distinct().ToList();
            if (!involved.Contains(stationId)) return BadRequest(new { message = "В заказе нет блюд для этой кухни" });
            var readyBefore = await _notifier.SnapshotAsync(order);

            var rows = await _context.OrderStationStatuses.Where(x => x.OrderId == id).ToListAsync();
            if (rows.Count == 0) rows = await EnsureMarksAsync(order, involved);
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

            // «Готово» на кухне — все её блюда готовы
            if (r.Status == OrderStatuses.Ready)
            {
                var stationItems = order.OrderItems.Where(i => itemStations.GetValueOrDefault(i.DishId, stations[0].Id) == stationId).ToList();
                var states = await StatesAsync(stationItems.Select(i => i.Id));
                foreach (var i in stationItems)
                {
                    var st = State(states, i.Id);
                    st.ReadyQuantity = i.Quantity;
                    st.UpdatedAt = DateTime.UtcNow;
                }
            }
            await _context.SaveChangesAsync();
            await RecalcAsync(order);
            await _notifier.NotifyAsync(order, readyBefore);
            return Ok(new { order.Id, order.Status });
        }

        public class VoidDecision
        {
            /// <summary>writeoff — списать продукты, keep — без списания</summary>
            public string Decision { get; set; } = string.Empty;
        }

        // POST: api/kitchen/voids/{id} — кухня решает по просьбе официанта убрать блюдо
        [HttpPost("voids/{id:int}")]
        [HasPermission(Permissions.Kitchen)]
        public async Task<IActionResult> ResolveVoid(int id, [FromBody] VoidDecision r, [FromServices] VoidService voids)
        {
            try { await voids.ResolveAsync(id, r.Decision, User.GetUserId()); return NoContent(); }
            catch (VoidService.VoidException ex) { return BadRequest(new { message = ex.Message }); }
        }

        // POST: api/kitchen/items/{itemId}/ready — блюдо (или ещё одна штука) готово; Delta −1 — отменить
        [HttpPost("items/{itemId:int}/ready")]
        [HasPermission(Permissions.Kitchen)]
        public Task<IActionResult> ItemReady(int itemId, [FromBody] ItemCountRequest r) => ChangeItemAsync(itemId, r.Delta, served: false);

        // POST: api/kitchen/items/{itemId}/served — блюдо отдано гостю (по мере готовности)
        // Блюда в зале отдаёт официант — только по своему счёту (менеджер — по любому)
        [HttpPost("items/{itemId:int}/served")]
        [HasPermission(Permissions.Kitchen, Permissions.Waiter, Permissions.ChecksAll)]
        public async Task<IActionResult> ItemServed(int itemId, [FromBody] ItemCountRequest r, [FromServices] ShiftService shifts)
        {
            if (!await shifts.HasAsync(User, Permissions.Kitchen) && !await shifts.HasAsync(User, Permissions.ChecksAll))
            {
                var item = await _context.OrderItems.AsNoTracking().Include(i => i.Order).FirstOrDefaultAsync(i => i.Id == itemId);
                var checkId = item?.CheckId ?? item?.Order?.CheckId;
                var me = User.GetUserId();
                if (checkId == null || !await _context.TableChecks.AnyAsync(c => c.Id == checkId && c.WaiterId == me))
                    return StatusCode(403, new { message = "Это блюдо не из вашего счёта" });
            }
            return await ChangeItemAsync(itemId, r.Delta, served: true);
        }

        private async Task<IActionResult> ChangeItemAsync(int itemId, int? delta, bool served)
        {
            var item = await _context.OrderItems.FirstOrDefaultAsync(i => i.Id == itemId);
            if (item == null) return NotFound(new { message = "Позиция не найдена" });
            var order = await _context.Orders.Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == item.OrderId);
            if (order == null || KitchenFlow.Rank(order.Status) < 0) return BadRequest(new { message = "Заказ уже закрыт" });

            var readyBefore = served ? null : await _notifier.SnapshotAsync(order);
            if (!served && !await _context.OrderStationStatuses.AnyAsync(x => x.OrderId == order.Id))
            {
                var all = await StationsAsync();
                var its = await ItemStationsAsync(order.OrderItems.Select(i => i.DishId), all[0].Id);
                await EnsureMarksAsync(order, order.OrderItems.Select(i => its.GetValueOrDefault(i.DishId, all[0].Id)).Distinct().ToList());
            }
            var states = await StatesAsync(new[] { itemId });
            var st = State(states, itemId);
            // Заказ целиком отмечен готовым — все его блюда готовы, даже без поштучных отметок
            if (KitchenFlow.Rank(order.Status) >= 3 && st.ReadyQuantity < item.Quantity) st.ReadyQuantity = item.Quantity;
            var (ready, servedQty) = served
                ? KitchenFlow.Clamp(item.Quantity, st.ReadyQuantity, delta.HasValue ? st.ServedQuantity + delta.Value : st.ReadyQuantity)
                : KitchenFlow.Clamp(item.Quantity, delta.HasValue ? st.ReadyQuantity + delta.Value : item.Quantity, st.ServedQuantity);
            st.ReadyQuantity = ready;
            st.ServedQuantity = servedQty;
            st.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await RecalcAsync(order);
            if (readyBefore != null) await _notifier.NotifyAsync(order, readyBefore);
            return Ok(new { order.Id, order.Status, readyQuantity = ready, servedQuantity = servedQty });
        }

        private async Task<Dictionary<int, OrderItemKitchenState>> StatesAsync(IEnumerable<int> itemIds)
        {
            var ids = itemIds.ToList();
            return await _context.OrderItemKitchenStates.Where(x => ids.Contains(x.OrderItemId)).ToDictionaryAsync(x => x.OrderItemId);
        }

        private OrderItemKitchenState State(Dictionary<int, OrderItemKitchenState> states, int itemId)
        {
            if (states.TryGetValue(itemId, out var st)) return st;
            st = new OrderItemKitchenState { OrderItemId = itemId };
            _context.OrderItemKitchenStates.Add(st);
            states[itemId] = st;
            return st;
        }

        /// <summary>
        /// Первая отметка кухни по заказу: до неё все части шли за общим статусом — фиксируем его
        /// для каждой кухни заказа, чтобы дальше каждая шла сама и чужие отметки её не двигали.
        /// </summary>
        private async Task<List<OrderStationStatus>> EnsureMarksAsync(Order order, List<int> involved)
        {
            var rank = KitchenFlow.Rank(order.Status);
            var status = rank >= 3 ? OrderStatuses.Ready : rank <= 0 ? OrderStatuses.New : order.Status;
            var rows = involved.Select(st => new OrderStationStatus
            {
                OrderId = order.Id, StationId = st, Status = status, UpdatedAt = DateTime.UtcNow, UpdatedByUserId = User.GetUserId()
            }).ToList();
            _context.OrderStationStatuses.AddRange(rows);
            await _context.SaveChangesAsync();
            return rows;
        }

        /// <summary>
        /// Заказ двинули целиком (общий экран, список заказов): кухни, которые отстают, догоняют его.
        /// Без отметок кухни и так идут за общим статусом.
        /// </summary>
        public static async Task LiftMarksAsync(ApplicationDbContext context, int orderId, string status, int? userId)
        {
            var rank = KitchenFlow.Rank(status);
            if (rank <= 0) return;
            var rows = await context.OrderStationStatuses.Where(x => x.OrderId == orderId).ToListAsync();
            foreach (var r in rows.Where(r => KitchenFlow.Rank(r.Status) < rank))
            {
                r.Status = rank >= 3 ? OrderStatuses.Ready : status;
                r.UpdatedAt = DateTime.UtcNow;
                r.UpdatedByUserId = userId;
            }
            await context.SaveChangesAsync();
        }

        /// <summary>
        /// Общий статус заказа по кухням и готовности блюд: все готовы — «Готов»; самовывоз и в зале,
        /// где всё уже отдано, — «Выполнен». Остальным экранам сообщаем об изменении.
        /// </summary>
        private async Task RecalcAsync(Order order)
        {
            var stations = await StationsAsync();
            var def = stations[0].Id;
            var itemStations = await ItemStationsAsync(order.OrderItems.Select(i => i.DishId), def);
            var marks = await _context.OrderStationStatuses.AsNoTracking().Where(x => x.OrderId == order.Id).ToDictionaryAsync(x => x.StationId, x => x.Status);
            var ids = order.OrderItems.Select(i => i.Id).ToList();
            var states = await _context.OrderItemKitchenStates.AsNoTracking()
                .Where(x => ids.Contains(x.OrderItemId)).ToDictionaryAsync(x => x.OrderItemId);

            var effective = order.OrderItems
                .GroupBy(i => itemStations.GetValueOrDefault(i.DishId, def))
                .Select(g => KitchenFlow.Effective(marks.GetValueOrDefault(g.Key), order.Status,
                    g.Sum(i => i.Quantity), g.Sum(i => Math.Min(i.Quantity, states.GetValueOrDefault(i.Id)?.ReadyQuantity ?? 0)), marks.Count > 0))
                .ToList();
            var overall = KitchenFlow.Overall(effective, order.Status);
            var changed = false;
            if (overall != order.Status)
                changed = await _statusService.ChangeStatusAsync(order, overall);

            var allServed = order.OrderItems.Count > 0 && order.OrderItems.All(i => (states.GetValueOrDefault(i.Id)?.ServedQuantity ?? 0) >= i.Quantity);
            if (allServed && order.Status == OrderStatuses.Ready && order.DeliveryType != "Доставка")
                changed = await _statusService.ChangeStatusAsync(order, OrderStatuses.Completed) || changed;

            if (!changed)
                _events.Publish(new OrderEvent("updated", order.Id, order.UserId, order.Status, null, DateTime.UtcNow));
        }

        /// <summary>Кухня каждого блюда: своя метка, иначе метка карточки, иначе кухня по умолчанию</summary>
        private Task<Dictionary<int, int>> ItemStationsAsync(IEnumerable<int> dishIds, int defaultStation) =>
            ItemStationsAsync(_context, dishIds, defaultStation);

        /// <summary>Кухня каждого блюда (по id блюда) — для кухни и экрана официанта</summary>
        public static async Task<Dictionary<int, int>> ItemStationsAsync(ApplicationDbContext context, IEnumerable<int> dishIds, int defaultStation)
        {
            var ids = dishIds.Distinct().ToList();
            var rows = await context.Dishes.AsNoTracking().Where(s => ids.Contains(s.Id))
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
            var itemStations = await ItemStationsAsync(orders.SelectMany(o => o.OrderItems).Select(i => i.DishId), defaultStation);
            var orderIds = orders.Select(o => o.Id).ToList();
            var marks = (await _context.OrderStationStatuses.AsNoTracking().Where(x => orderIds.Contains(x.OrderId)).ToListAsync())
                .ToDictionary(x => (x.OrderId, x.StationId), x => x.Status);
            var itemIds = orders.SelectMany(o => o.OrderItems).Select(i => i.Id).ToList();
            var itemStates = await _context.OrderItemKitchenStates.AsNoTracking().Where(x => itemIds.Contains(x.OrderItemId))
                .ToDictionaryAsync(x => x.OrderItemId);
            // Заказ целиком готов — все его блюда готовы, даже без поштучных отметок
            var orderStatus = orders.ToDictionary(o => o.Id, o => o.Status);
            int ReadyOf(OrderItem i) => KitchenFlow.Rank(orderStatus.GetValueOrDefault(i.OrderId, "")) >= 3 ? i.Quantity : Math.Min(i.Quantity, itemStates.GetValueOrDefault(i.Id)?.ReadyQuantity ?? 0);
            int ServedOf(OrderItem i) => Math.Min(ReadyOf(i), itemStates.GetValueOrDefault(i.Id)?.ServedQuantity ?? 0);
            int StationOf(int dishId) => itemStations.GetValueOrDefault(dishId, defaultStation);

            // Просьбы официантов убрать блюда, которые уже готовят
            var pendingVoids = (await _context.OrderItemVoids.AsNoTracking()
                    .Where(v => orderIds.Contains(v.OrderId) && v.Status == VoidStatuses.Pending).OrderBy(v => v.RequestedAt).ToListAsync())
                .GroupBy(v => v.OrderId).ToDictionary(g => g.Key, g => g.ToList());

            // Заказы официантов: стол и кто принял
            var checkIds = orders.Where(o => o.CheckId != null).Select(o => o.CheckId!.Value).Distinct().ToList();
            var checks = await _context.TableChecks.AsNoTracking().Include(c => c.Table)
                .Where(c => checkIds.Contains(c.Id))
                .Select(c => new { c.Id, Table = c.Table!.Name })
                .ToDictionaryAsync(c => c.Id, c => c.Table);
            var staffIds = orders.Where(o => o.CashierId != null).Select(o => o.CashierId!.Value).Distinct().ToList();
            var staff = (await _context.Users.AsNoTracking().Where(u => staffIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.FirstName, u.LastName }).ToListAsync())
                .ToDictionary(u => u.Id, u => string.Join(' ', new[] { u.FirstName, u.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))));

            var marked = marks.Keys.Select(k => k.OrderId).ToHashSet();
            var nowUtc = DateTime.UtcNow;
            var result = orders.Select(o =>
            {
                var scheduledUtc = o.ScheduledFor?.ToUniversalTime();
                var orderStations = o.OrderItems.Select(i => StationOf(i.DishId)).Distinct()
                    .OrderBy(id => stations.FindIndex(s => s.Id == id))
                    .Select(id => new
                    {
                        Id = id,
                        Name = names.GetValueOrDefault(id, "Кухня"),
                        Status = KitchenFlow.Effective(marks.GetValueOrDefault((o.Id, id)), o.Status,
                            o.OrderItems.Where(i => StationOf(i.DishId) == id).Sum(i => i.Quantity),
                            o.OrderItems.Where(i => StationOf(i.DishId) == id).Sum(ReadyOf),
                            marked.Contains(o.Id))
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
                    VoidRequests = pendingVoids.GetValueOrDefault(o.Id, new()).Select(v => new { v.Id, v.OrderItemId, v.Name, v.Quantity, v.Reason }),
                    TableName = o.CheckId != null ? checks.GetValueOrDefault(o.CheckId.Value) : null,
                    WaiterName = o.CheckId != null && o.CashierId != null ? staff.GetValueOrDefault(o.CashierId.Value) : null,
                    Items = o.OrderItems.OrderBy(i => i.Id).Select(i => new
                    {
                        i.Id,
                        ReadyQuantity = ReadyOf(i),
                        ServedQuantity = ServedOf(i),
                        i.DishId,
                        StationId = StationOf(i.DishId),
                        i.Name,
                        i.Quantity,
                        i.Weight,
                        Addons = i.SelectedAddons.Select(a => new { a.AddonId, a.AddonName, a.Quantity })
                    })
                };
            });

            return Ok(result);
        }

        // GET: api/kitchen/techcard/{dishId}?addons=1,2 — техкарта блюда (и выбранных добавок) для повара, без себестоимости
        [HttpGet("techcard/{dishId:int}")]
        [HasPermission(Permissions.Kitchen)]
        public async Task<IActionResult> GetTechCard(int dishId, [FromQuery] string? addons)
        {
            var dish = await _context.Dishes.AsNoTracking()
                .Where(s => s.Id == dishId)
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

            // Фото — у самого варианта, иначе у карточки (главное, иначе первое)
            var photoOwners = dish.ParentId.HasValue ? new[] { dish.Id, dish.ParentId.Value } : new[] { dish.Id };
            var photos = await _context.DishImages.AsNoTracking()
                .Where(i => photoOwners.Contains(i.DishId))
                .Select(i => new { i.DishId, i.FilePath, i.IsPrimary, i.Id })
                .ToListAsync();
            var image = photos
                .OrderBy(p => p.DishId == dish.Id ? 0 : 1).ThenByDescending(p => p.IsPrimary).ThenBy(p => p.Id)
                .Select(p => p.FilePath).FirstOrDefault();

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
                image,
                dish = dishCard,
                addons = addonCards,
                semis = KitchenTechCards.SemiCards(all, calc)
            });
        }
    }
}
