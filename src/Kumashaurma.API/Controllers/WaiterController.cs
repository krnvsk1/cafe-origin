using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Authorization;
using Kumashaurma.API.Data;
using Kumashaurma.API.Extensions;
using Kumashaurma.API.Models;
using Kumashaurma.API.Services;

namespace Kumashaurma.API.Controllers
{
    /// <summary>
    /// Официант: столы, чеки столов (открыть → дозаказывать → закрыть при оплате).
    /// Каждая отправка на кухню — отдельный заказ кухни с номером стола и именем официанта.
    /// </summary>
    [ApiController]
    [Route("api/waiter")]
    [HasPermission(Permissions.Waiter, Permissions.TablesManage)]
    public class WaiterController : ControllerBase
    {
        public const string Source = "waiter";
        /// <summary>Оплата при закрытии чека</summary>
        public const string PayLater = "check";
        private const int DefaultTables = 10;

        private readonly ApplicationDbContext _context;
        private readonly IOrderStatusService _statusService;
        private readonly IOrderEventBus _events;
        private readonly ILogger<WaiterController> _logger;

        private readonly VoidService _voids;

        public WaiterController(ApplicationDbContext context, IOrderStatusService statusService, IOrderEventBus events, VoidService voids, ILogger<WaiterController> logger)
        {
            _voids = voids;
            _context = context;
            _statusService = statusService;
            _events = events;
            _logger = logger;
        }

        public class TableRequest
        {
            public int? Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public bool IsActive { get; set; } = true;
        }

        public class OpenRequest
        {
            public int TableId { get; set; }
            public int? Guests { get; set; }
            public string? Comment { get; set; }
            public string? Label { get; set; }
        }

        public class RoundRequest
        {
            public List<PosController.PosItemRequest> Items { get; set; } = new();
            public string? Comment { get; set; }
        }

        public class VoidRequest
        {
            public int Quantity { get; set; } = 1;
            public string Reason { get; set; } = string.Empty;
        }

        public class SplitLine
        {
            public int ItemId { get; set; }
            public int Quantity { get; set; }
        }

        public class SplitRequest
        {
            public List<SplitLine> Items { get; set; } = new();
            /// <summary>В какой счёт перенести; null — новый счёт за этим же столом</summary>
            public int? TargetCheckId { get; set; }
            public string? Label { get; set; }
        }

        public class MoveRequest
        {
            public int TableId { get; set; }
        }

        public class UpdateCheckRequest
        {
            public string? Label { get; set; }
            public int? Guests { get; set; }
        }

        public class CloseRequest
        {
            /// <summary>cash или card</summary>
            public string PaymentMethod { get; set; } = "cash";
            public decimal? CashReceived { get; set; }
        }

        // ==================== Столы ====================

        private async Task<List<DiningTable>> TablesAsync(bool onlyActive)
        {
            if (!await _context.DiningTables.AnyAsync())
            {
                for (var i = 1; i <= DefaultTables; i++)
                    _context.DiningTables.Add(new DiningTable { Name = i.ToString(), SortOrder = i });
                await _context.SaveChangesAsync();
            }
            var q = _context.DiningTables.AsNoTracking();
            if (onlyActive) q = q.Where(t => t.IsActive);
            return await q.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync();
        }

        // GET: api/waiter/tables — столы и открытые на них счета (за столом их может быть несколько)
        [HttpGet("tables")]
        public async Task<IActionResult> Tables()
        {
            var me = User.GetUserId();
            var tables = await TablesAsync(onlyActive: true);
            var open = await _context.TableChecks.AsNoTracking().Where(c => c.Status == CheckStatuses.Open).OrderBy(c => c.OpenedAt).ToListAsync();
            var waiterNames = await NamesAsync(open.Select(c => c.WaiterId));
            var lines = await LinesAsync(open.Select(c => c.Id).ToList());

            return Ok(tables.Select(t => new
            {
                t.Id,
                t.Name,
                checks = open.Where(c => c.TableId == t.Id).Select(c =>
                {
                    var mine = lines.Where(l => l.CheckId == c.Id && l.Order.Status != OrderStatuses.Cancelled).ToList();
                    return new
                    {
                        c.Id,
                        c.Label,
                        c.OpenedAt,
                        c.Guests,
                        waiter = waiterNames.GetValueOrDefault(c.WaiterId ?? 0),
                        isMine = c.WaiterId == me,
                        total = mine.Sum(l => LineTotal(l.Item)),
                        // Есть готовые, но не отданные блюда — пора нести
                        ready = mine.Count(l => l.Order.Status == OrderStatuses.Ready),
                        cooking = mine.Select(l => l.Order).Distinct().Count(o => o.Status is OrderStatuses.New or OrderStatuses.Accepted or OrderStatuses.Preparing)
                    };
                })
            }));
        }

        /// <summary>Позиция заказа и счёт, к которому она относится (перенесённая при делении — к своему)</summary>
        private record CheckLine(OrderItem Item, Order Order, int CheckId);

        /// <summary>Все позиции указанных счетов (с заказами), включая перенесённые из других счетов</summary>
        private async Task<List<CheckLine>> LinesAsync(List<int> checkIds)
        {
            if (checkIds.Count == 0) return new();
            var orders = await _context.Orders.AsNoTracking()
                .Include(o => o.OrderItems).ThenInclude(i => i.SelectedAddons)
                .Where(o => (o.CheckId != null && checkIds.Contains(o.CheckId.Value))
                            || o.OrderItems.Any(i => i.CheckId != null && checkIds.Contains(i.CheckId.Value)))
                .ToListAsync();
            return orders.SelectMany(o => o.OrderItems.Select(i => new CheckLine(i, o, i.CheckId ?? o.CheckId ?? 0)))
                .Where(l => checkIds.Contains(l.CheckId))
                .ToList();
        }

        public static decimal LineTotal(OrderItem i) => (i.Price + i.SelectedAddons.Sum(a => a.Price * a.Quantity)) * i.Quantity;

        // GET: api/waiter/tables/all — все столы для настройки
        [HttpGet("tables/all")]
        [HasPermission(Permissions.TablesManage)]
        public async Task<IActionResult> AllTables() =>
            Ok((await TablesAsync(onlyActive: false)).Select(t => new { t.Id, t.Name, t.IsActive }));

        // PUT: api/waiter/tables — список столов целиком (порядок — как в списке)
        [HttpPut("tables")]
        [HasPermission(Permissions.TablesManage)]
        public async Task<IActionResult> SaveTables([FromBody] List<TableRequest> list)
        {
            if (list.Count == 0 || list.Count > 200) return BadRequest(new { message = "Столов — от 1 до 200" });
            var names = list.Select(t => (t.Name ?? "").Trim()).ToList();
            if (names.Any(n => n.Length == 0 || n.Length > 40)) return BadRequest(new { message = "Название стола — от 1 до 40 символов" });
            if (names.GroupBy(n => n.ToLower()).Any(g => g.Count() > 1)) return BadRequest(new { message = "Названия столов повторяются" });

            var existing = await _context.DiningTables.ToListAsync();
            var keep = new HashSet<int>();
            for (var i = 0; i < list.Count; i++)
            {
                var r = list[i];
                var t = r.Id.HasValue ? existing.FirstOrDefault(x => x.Id == r.Id) : null;
                if (t == null)
                {
                    t = new DiningTable();
                    _context.DiningTables.Add(t);
                }
                else keep.Add(t.Id);
                t.Name = names[i];
                t.SortOrder = i + 1;
                t.IsActive = r.IsActive;
            }
            // Убранные из списка — выключаем (у них может быть история чеков)
            foreach (var t in existing.Where(x => !keep.Contains(x.Id))) t.IsActive = false;
            await _context.SaveChangesAsync();
            return await AllTables();
        }

        // ==================== Чеки ====================

        // POST: api/waiter/checks — открыть чек на стол (если уже открыт — вернуть его)
        [HttpPost("checks")]
        [HasPermission(Permissions.Waiter)]
        public async Task<IActionResult> Open([FromBody] OpenRequest r)
        {
            var table = await _context.DiningTables.FirstOrDefaultAsync(t => t.Id == r.TableId && t.IsActive);
            if (table == null) return BadRequest(new { message = "Стол не найден" });
            if (r.Guests is < 1 or > 100) return BadRequest(new { message = "Гостей — от 1 до 100" });
            // За столом может быть несколько счетов: второй и следующие подписываем «Счёт N»
            var openHere = await _context.TableChecks.CountAsync(c => c.TableId == r.TableId && c.Status == CheckStatuses.Open);
            var label = string.IsNullOrWhiteSpace(r.Label) ? (openHere > 0 ? $"Счёт {openHere + 1}" : null) : Trunc(r.Label.Trim(), 40);

            var check = new TableCheck
            {
                TableId = table.Id,
                WaiterId = User.GetUserId(),
                Guests = r.Guests,
                Label = label,
                Comment = string.IsNullOrWhiteSpace(r.Comment) ? null : r.Comment.Trim(),
                OpenedAt = DateTime.UtcNow
            };
            _context.TableChecks.Add(check);
            await _context.SaveChangesAsync();
            return Ok(new { check.Id, alreadyOpen = false });
        }

        // GET: api/waiter/checks/{id} — чек: заказы на кухню, готовность блюд, сумма
        [HttpGet("checks/{id:int}")]
        [HasPermission(Permissions.Waiter)]
        public async Task<IActionResult> Get(int id)
        {
            var c = await _context.TableChecks.AsNoTracking().Include(x => x.Table).FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound(new { message = "Чек не найден" });
            var waiter = (await NamesAsync(new[] { c.WaiterId })).GetValueOrDefault(c.WaiterId ?? 0);
            var lines = await LinesAsync(new List<int> { id });
            var itemIds = lines.Select(l => l.Item.Id).ToList();
            var states = await _context.OrderItemKitchenStates.AsNoTracking().Where(x => itemIds.Contains(x.OrderItemId)).ToDictionaryAsync(x => x.OrderItemId);
            var active = lines.Where(l => l.Order.Status != OrderStatuses.Cancelled).ToList();
            int Ready(CheckLine l) => l.Order.Status is OrderStatuses.Ready or OrderStatuses.Completed
                ? l.Item.Quantity : Math.Min(l.Item.Quantity, states.GetValueOrDefault(l.Item.Id)?.ReadyQuantity ?? 0);
            int Served(CheckLine l) => l.Order.Status == OrderStatuses.Completed
                ? l.Item.Quantity : Math.Min(l.Item.Quantity, states.GetValueOrDefault(l.Item.Id)?.ServedQuantity ?? 0);

            var voids = await _context.OrderItemVoids.AsNoTracking()
                .Where(v => v.CheckId == id || (v.OrderItemId != null && itemIds.Contains(v.OrderItemId.Value)))
                .OrderBy(v => v.RequestedAt).ToListAsync();
            var pendingByItem = voids.Where(v => v.Status == VoidStatuses.Pending && v.OrderItemId != null)
                .GroupBy(v => v.OrderItemId!.Value).ToDictionary(g => g.Key, g => g.Sum(v => v.Quantity));

            // Другие открытые счета за этим столом — для деления и объединения
            var siblings = await _context.TableChecks.AsNoTracking()
                .Where(x => x.TableId == c.TableId && x.Status == CheckStatuses.Open && x.Id != id)
                .OrderBy(x => x.OpenedAt).Select(x => new { x.Id, x.Label }).ToListAsync();

            return Ok(new
            {
                c.Id,
                c.Status,
                c.Label,
                table = new { c.Table!.Id, c.Table.Name },
                waiter,
                isMine = c.WaiterId == User.GetUserId(),
                c.Guests,
                c.Comment,
                c.OpenedAt,
                c.ClosedAt,
                c.PaymentMethod,
                total = c.Status == CheckStatuses.Closed ? c.Total : active.Sum(l => LineTotal(l.Item)),
                // Сколько блюд ещё не отдано (при закрытии предупреждаем)
                notServed = active.Sum(l => l.Item.Quantity - Served(l)),
                siblings,
                voids = voids.Select(v => new { v.Id, v.Name, v.Quantity, v.Amount, v.Reason, v.Status, v.WriteOff, v.RequestedAt }),
                orders = lines.GroupBy(l => l.Order).OrderBy(g => g.Key.CreatedAt).Select(g => new
                {
                    g.Key.Id,
                    g.Key.Status,
                    total = g.Sum(l => LineTotal(l.Item)),
                    g.Key.Notes,
                    g.Key.CreatedAt,
                    items = g.OrderBy(l => l.Item.Id).Select(l => new
                    {
                        l.Item.Id,
                        l.Item.Name,
                        l.Item.Quantity,
                        l.Item.Price,
                        lineTotal = LineTotal(l.Item),
                        addons = l.Item.SelectedAddons.Select(a => new { a.AddonName, a.Quantity, a.Price }),
                        pendingVoid = pendingByItem.GetValueOrDefault(l.Item.Id),
                        readyQuantity = Ready(l),
                        servedQuantity = Served(l),
                        // Перенесена из другого счёта или в этот счёт пришла не своей подачей
                        moved = l.Item.CheckId != null && l.Order.CheckId != id
                    })
                })
            });
        }

        // POST: api/waiter/checks/{id}/orders — дозаказ: новые блюда уходят на кухню отдельным заказом
        [HttpPost("checks/{id:int}/orders")]
        [HasPermission(Permissions.Waiter)]
        public async Task<IActionResult> AddRound(int id, [FromBody] RoundRequest r)
        {
            var c = await _context.TableChecks.Include(x => x.Table).FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound(new { message = "Чек не найден" });
            if (c.Status != CheckStatuses.Open) return BadRequest(new { message = "Чек уже закрыт" });
            if (r.Items.Count == 0) return BadRequest(new { message = "Добавьте блюда" });
            if (r.Items.Count > 100) return BadRequest(new { message = "Слишком много позиций за раз" });
            if (r.Items.Any(i => i.Quantity <= 0 || i.Quantity > 99 || i.Addons.Any(a => a.Quantity <= 0 || a.Quantity > 20)))
                return BadRequest(new { message = "Проверьте количество" });

            var (items, total, error) = await PosController.BuildItemsAsync(_context, r.Items, SalesChannels.DineIn);
            if (error != null) return BadRequest(new { message = error });

            var waiterId = User.GetUserId();
            var waiterName = (await NamesAsync(new[] { waiterId })).GetValueOrDefault(waiterId ?? 0);
            var order = new Order
            {
                CustomerName = $"Стол {c.Table!.Name}",
                Phone = string.Empty,
                Address = "В зале",
                DeliveryType = SalesChannels.DineIn,
                Notes = string.IsNullOrWhiteSpace(r.Comment) ? null : r.Comment.Trim(),
                Status = OrderStatuses.New,
                PaymentMethod = PayLater,
                PaymentStatus = "unpaid",
                Source = Source,
                CashierId = waiterId,
                CheckId = c.Id,
                Total = total,
                CreatedAt = DateTime.UtcNow,
                AccessToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()
            };
            foreach (var item in items!) order.OrderItems.Add(item);
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();
            _events.Publish(new OrderEvent("created", order.Id, null, order.Status, null, DateTime.UtcNow));
            _logger.LogInformation("🍽 Официант {Waiter}: стол {Table}, заказ #{OrderId} на {Total} ₽", waiterName, c.Table.Name, order.Id, total);
            return Ok(new { order.Id, order.Total });
        }

        // POST: api/waiter/checks/{id}/orders/{orderId}/cancel — отменить подачу целиком, пока кухня её не приняла
        [HttpPost("checks/{id:int}/orders/{orderId:int}/cancel")]
        [HasPermission(Permissions.Waiter)]
        public async Task<IActionResult> CancelRound(int id, int orderId)
        {
            var order = await _context.Orders.AsNoTracking().Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null) return NotFound(new { message = "Заказ не найден" });
            if (order.Status != OrderStatuses.New)
                return BadRequest(new { message = "Кухня уже приняла подачу — убирайте блюда по одному, кухня подтвердит" });
            try
            {
                foreach (var i in order.OrderItems.Where(i => (i.CheckId ?? order.CheckId) == id).ToList())
                    await _voids.RequestAsync(i.Id, i.Quantity, "Отмена подачи", User.GetUserId());
            }
            catch (VoidService.VoidException ex) { return BadRequest(new { message = ex.Message }); }
            return NoContent();
        }

        // POST: api/waiter/items/{itemId}/void — убрать блюдо (или часть штук) из счёта с причиной
        [HttpPost("items/{itemId:int}/void")]
        [HasPermission(Permissions.Waiter)]
        public async Task<IActionResult> VoidItem(int itemId, [FromBody] VoidRequest r)
        {
            try
            {
                var v = await _voids.RequestAsync(itemId, r.Quantity, r.Reason, User.GetUserId());
                return Ok(new { v.Id, v.Status, needsKitchen = v.Status == VoidStatuses.Pending });
            }
            catch (VoidService.VoidException ex) { return BadRequest(new { message = ex.Message }); }
        }

        // POST: api/waiter/checks/{id}/close — гость оплатил: чек закрыт, заказы оплачены
        [HttpPost("checks/{id:int}/close")]
        [HasPermission(Permissions.Waiter)]
        public async Task<IActionResult> Close(int id, [FromBody] CloseRequest r)
        {
            var method = r.PaymentMethod?.Trim().ToLowerInvariant();
            if (method is not ("cash" or "card")) return BadRequest(new { message = "Выберите способ оплаты" });
            var c = await _context.TableChecks.FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound(new { message = "Чек не найден" });
            if (c.Status != CheckStatuses.Open) return BadRequest(new { message = "Чек уже закрыт" });

            var lines = (await LinesAsync(new List<int> { id })).Where(l => l.Order.Status != OrderStatuses.Cancelled).ToList();
            if (lines.Count == 0) return BadRequest(new { message = "В счёте нет блюд — его можно просто отменить" });
            var total = lines.Sum(l => LineTotal(l.Item));
            if (method == "cash" && r.CashReceived.HasValue && r.CashReceived.Value < total)
                return BadRequest(new { message = $"Получено меньше суммы счёта ({total:0.##} ₽)" });

            var now = DateTime.UtcNow;
            c.Status = CheckStatuses.Closed;
            c.ClosedAt = now;
            c.PaymentMethod = method;
            c.Total = total;
            await _context.SaveChangesAsync();

            // Заказ кухни оплачен, когда закрыты все счета, куда попали его блюда
            var orderIds = lines.Select(l => l.Order.Id).Distinct().ToList();
            var orders = await _context.Orders.Include(o => o.OrderItems).Where(o => orderIds.Contains(o.Id)).ToListAsync();
            var related = orders.SelectMany(o => o.OrderItems.Select(i => i.CheckId ?? o.CheckId)).Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
            var relatedChecks = await _context.TableChecks.AsNoTracking().Where(x => related.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            foreach (var o in orders)
            {
                var checks = o.OrderItems.Select(i => i.CheckId ?? o.CheckId).Where(x => x != null).Select(x => relatedChecks[x!.Value]).Distinct().ToList();
                if (checks.Any(x => x.Status == CheckStatuses.Open)) continue;
                var methods = checks.Where(x => x.Status == CheckStatuses.Closed).Select(x => x.PaymentMethod).Distinct().ToList();
                o.PaymentMethod = methods.Count == 1 ? methods[0] ?? method : "mixed";
                o.PaymentStatus = "paid";
                o.PaidAt = now;
            }
            await _context.SaveChangesAsync();
            foreach (var o in orders)
                _events.Publish(new OrderEvent("updated", o.Id, o.UserId, o.Status, null, now));
            _logger.LogInformation("🍽 Счёт #{CheckId} закрыт: {Total} ₽ ({Method})", c.Id, total, method);
            return Ok(new
            {
                c.Id,
                total,
                change = method == "cash" && r.CashReceived.HasValue ? r.CashReceived.Value - total : (decimal?)null,
                orderIds
            });
        }

        // POST: api/waiter/checks/{id}/cancel — отменить пустой чек (стол освобождается)
        [HttpPost("checks/{id:int}/cancel")]
        [HasPermission(Permissions.Waiter)]
        public async Task<IActionResult> CancelCheck(int id)
        {
            var c = await _context.TableChecks.FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound(new { message = "Чек не найден" });
            if (c.Status != CheckStatuses.Open) return BadRequest(new { message = "Чек уже закрыт" });
            if ((await LinesAsync(new List<int> { id })).Any(l => l.Order.Status != OrderStatuses.Cancelled))
                return BadRequest(new { message = "В счёте есть блюда — перенесите их, отмените подачу или закройте счёт оплатой" });
            c.Status = CheckStatuses.Cancelled;
            c.ClosedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // PUT: api/waiter/checks/{id} — подпись счёта и число гостей
        [HttpPut("checks/{id:int}")]
        [HasPermission(Permissions.Waiter)]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateCheckRequest r)
        {
            var c = await _context.TableChecks.FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound(new { message = "Счёт не найден" });
            if (r.Guests is < 1 or > 100) return BadRequest(new { message = "Гостей — от 1 до 100" });
            c.Label = string.IsNullOrWhiteSpace(r.Label) ? null : Trunc(r.Label.Trim(), 40);
            if (r.Guests.HasValue) c.Guests = r.Guests;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // POST: api/waiter/checks/{id}/move — пересадить гостей: счёт переходит на другой стол
        [HttpPost("checks/{id:int}/move")]
        [HasPermission(Permissions.Waiter)]
        public async Task<IActionResult> Move(int id, [FromBody] MoveRequest r)
        {
            var c = await _context.TableChecks.FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound(new { message = "Счёт не найден" });
            if (c.Status != CheckStatuses.Open) return BadRequest(new { message = "Счёт уже закрыт" });
            var table = await _context.DiningTables.FirstOrDefaultAsync(t => t.Id == r.TableId && t.IsActive);
            if (table == null) return BadRequest(new { message = "Стол не найден" });
            if (table.Id == c.TableId) return NoContent();
            c.TableId = table.Id;
            // Подписи заказов на кухне и в кассе
            var orders = await _context.Orders.Where(o => o.CheckId == id).ToListAsync();
            foreach (var o in orders) o.CustomerName = $"Стол {table.Name}";
            await _context.SaveChangesAsync();
            foreach (var o in orders) _events.Publish(new OrderEvent("updated", o.Id, o.UserId, o.Status, null, DateTime.UtcNow));
            return NoContent();
        }

        // POST: api/waiter/checks/{id}/split — разделить счёт: перенести блюда (можно часть штук) в другой или новый счёт
        [HttpPost("checks/{id:int}/split")]
        [HasPermission(Permissions.Waiter)]
        public async Task<IActionResult> Split(int id, [FromBody] SplitRequest r)
        {
            var c = await _context.TableChecks.FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound(new { message = "Счёт не найден" });
            if (c.Status != CheckStatuses.Open) return BadRequest(new { message = "Счёт уже закрыт" });
            var moves = r.Items.Where(x => x.Quantity > 0).GroupBy(x => x.ItemId).Select(g => (ItemId: g.Key, Quantity: g.Sum(x => x.Quantity))).ToList();
            if (moves.Count == 0) return BadRequest(new { message = "Выберите, что перенести" });

            TableCheck target;
            if (r.TargetCheckId.HasValue)
            {
                if (r.TargetCheckId == id) return BadRequest(new { message = "Это тот же счёт" });
                target = await _context.TableChecks.FirstOrDefaultAsync(x => x.Id == r.TargetCheckId) ?? null!;
                if (target == null || target.Status != CheckStatuses.Open) return BadRequest(new { message = "Счёт, куда переносим, не найден или закрыт" });
            }
            else
            {
                var openHere = await _context.TableChecks.CountAsync(x => x.TableId == c.TableId && x.Status == CheckStatuses.Open);
                target = new TableCheck
                {
                    TableId = c.TableId,
                    WaiterId = User.GetUserId(),
                    Label = string.IsNullOrWhiteSpace(r.Label) ? $"Счёт {openHere + 1}" : Trunc(r.Label.Trim(), 40),
                    OpenedAt = DateTime.UtcNow
                };
                _context.TableChecks.Add(target);
                await _context.SaveChangesAsync();
            }

            var lines = await LinesAsync(new List<int> { id });
            var itemIds = moves.Select(m => m.ItemId).ToList();
            var items = await _context.OrderItems.Include(i => i.SelectedAddons).Include(i => i.Order).Where(i => itemIds.Contains(i.Id)).ToListAsync();
            var states = await _context.OrderItemKitchenStates.Where(x => itemIds.Contains(x.OrderItemId)).ToDictionaryAsync(x => x.OrderItemId);
            foreach (var (itemId, qty) in moves)
            {
                var item = items.FirstOrDefault(i => i.Id == itemId);
                if (item == null || lines.All(l => l.Item.Id != itemId)) return BadRequest(new { message = "Позиция не из этого счёта" });
                if (item.Order!.Status == OrderStatuses.Cancelled) return BadRequest(new { message = $"«{item.Name}» — подача отменена" });
                if (qty > item.Quantity) return BadRequest(new { message = $"«{item.Name}»: в счёте только {item.Quantity} шт." });

                if (qty == item.Quantity)
                {
                    item.CheckId = target.Id;
                    continue;
                }
                // Часть штук — отдельной строкой того же заказа; готовность делим между строками
                var copy = new OrderItem
                {
                    OrderId = item.OrderId,
                    ShawarmaId = item.ShawarmaId,
                    Name = item.Name,
                    Quantity = qty,
                    Price = item.Price,
                    CheckId = target.Id
                };
                foreach (var a in item.SelectedAddons)
                    copy.SelectedAddons.Add(new OrderItemAddon
                    {
                        AddonId = a.AddonId, AddonName = a.AddonName, AddonCategoryId = a.AddonCategoryId,
                        AddonCategoryName = a.AddonCategoryName, Price = a.Price, Quantity = a.Quantity
                    });
                item.Quantity -= qty;
                _context.OrderItems.Add(copy);
                await _context.SaveChangesAsync();
                if (states.TryGetValue(item.Id, out var st))
                {
                    var ready = Math.Min(qty, st.ReadyQuantity);
                    var served = Math.Min(ready, st.ServedQuantity);
                    st.ReadyQuantity -= ready;
                    st.ServedQuantity = Math.Min(st.ServedQuantity - served, st.ReadyQuantity);
                    _context.OrderItemKitchenStates.Add(new OrderItemKitchenState { OrderItemId = copy.Id, ReadyQuantity = ready, ServedQuantity = served });
                }
            }
            await _context.SaveChangesAsync();
            foreach (var o in items.Select(i => i.Order!).Distinct())
                _events.Publish(new OrderEvent("updated", o.Id, o.UserId, o.Status, null, DateTime.UtcNow));
            return Ok(new { targetCheckId = target.Id });
        }

        // ==================== Схема зала ====================

        public const string FloorKey = "floor_plan";
        private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

        public class FloorWall
        {
            public string Id { get; set; } = string.Empty;
            public int X { get; set; }
            public int Y { get; set; }
            public int W { get; set; }
            public int H { get; set; }
            /// <summary>wall — стена/перегородка, bar — барная стойка, plant — декор</summary>
            public string Kind { get; set; } = "wall";
        }

        public class FloorTable
        {
            public int TableId { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
            public int W { get; set; } = 80;
            public int H { get; set; } = 80;
            /// <summary>rect или round</summary>
            public string Shape { get; set; } = "rect";
        }

        public class FloorPlan
        {
            public int Width { get; set; } = 1000;
            public int Height { get; set; } = 700;
            public List<FloorWall> Walls { get; set; } = new();
            public List<FloorTable> Tables { get; set; } = new();
        }

        // GET: api/waiter/floor — схема зала (столы и перегородки)
        [HttpGet("floor")]
        public async Task<IActionResult> GetFloor()
        {
            var row = await _context.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == FloorKey);
            var plan = row == null ? new FloorPlan() : System.Text.Json.JsonSerializer.Deserialize<FloorPlan>(row.Value, Json) ?? new FloorPlan();
            return Ok(plan);
        }

        // PUT: api/waiter/floor — сохранить схему зала
        [HttpPut("floor")]
        [HasPermission(Permissions.TablesManage)]
        public async Task<IActionResult> SaveFloor([FromBody] FloorPlan plan)
        {
            if (plan.Width is < 200 or > 5000 || plan.Height is < 200 or > 5000) return BadRequest(new { message = "Размер зала — от 200 до 5000" });
            if (plan.Walls.Count > 300 || plan.Tables.Count > 300) return BadRequest(new { message = "Слишком много элементов" });
            bool Inside(int x, int y, int w, int h) => w >= 10 && h >= 10 && x >= 0 && y >= 0 && x + w <= plan.Width && y + h <= plan.Height;
            if (plan.Walls.Any(w => !Inside(w.X, w.Y, w.W, w.H)) || plan.Tables.Any(t => !Inside(t.X, t.Y, t.W, t.H)))
                return BadRequest(new { message = "Элемент выходит за границы зала" });
            if (plan.Walls.Any(w => w.Kind is not ("wall" or "bar" or "plant")) || plan.Tables.Any(t => t.Shape is not ("rect" or "round")))
                return BadRequest(new { message = "Неизвестный элемент схемы" });
            var ids = await _context.DiningTables.Select(t => t.Id).ToListAsync();
            plan.Tables = plan.Tables.Where(t => ids.Contains(t.TableId)).GroupBy(t => t.TableId).Select(g => g.Last()).ToList();
            foreach (var w in plan.Walls) w.Id = string.IsNullOrWhiteSpace(w.Id) ? Guid.NewGuid().ToString("N")[..8] : Trunc(w.Id, 20);

            var row = await _context.AppSettings.FirstOrDefaultAsync(x => x.Key == FloorKey);
            if (row == null)
            {
                row = new AppSetting { Key = FloorKey };
                _context.AppSettings.Add(row);
            }
            row.Value = System.Text.Json.JsonSerializer.Serialize(plan, Json);
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedByUserId = User.GetUserId();
            await _context.SaveChangesAsync();
            return Ok(plan);
        }

        private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];

        private async Task<Dictionary<int, string>> NamesAsync(IEnumerable<int?> ids)
        {
            var list = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
            var users = await _context.Users.AsNoTracking().Where(u => list.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.PhoneNumber }).ToListAsync();
            return users.ToDictionary(u => u.Id, u =>
            {
                var n = string.Join(' ', new[] { u.FirstName, u.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
                return n.Length > 0 ? n : u.PhoneNumber ?? $"сотрудник #{u.Id}";
            });
        }
    }
}
