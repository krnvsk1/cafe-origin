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
    [HasPermission(Permissions.Waiter, Permissions.Settings)]
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

        public WaiterController(ApplicationDbContext context, IOrderStatusService statusService, IOrderEventBus events, ILogger<WaiterController> logger)
        {
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
        }

        public class RoundRequest
        {
            public List<PosController.PosItemRequest> Items { get; set; } = new();
            public string? Comment { get; set; }
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

        // GET: api/waiter/tables — столы и открытые на них чеки
        [HttpGet("tables")]
        public async Task<IActionResult> Tables()
        {
            var me = User.GetUserId();
            var tables = await TablesAsync(onlyActive: true);
            var open = await _context.TableChecks.AsNoTracking().Where(c => c.Status == CheckStatuses.Open).ToListAsync();
            var waiterNames = await NamesAsync(open.Select(c => c.WaiterId));
            var checkIds = open.Select(c => c.Id).ToList();
            var orders = await _context.Orders.AsNoTracking()
                .Where(o => o.CheckId != null && checkIds.Contains(o.CheckId.Value) && o.Status != OrderStatuses.Cancelled)
                .Select(o => new { o.CheckId, o.Status, o.Total })
                .ToListAsync();

            return Ok(tables.Select(t =>
            {
                var c = open.FirstOrDefault(x => x.TableId == t.Id);
                var co = c == null ? null : orders.Where(o => o.CheckId == c.Id).ToList();
                return new
                {
                    t.Id,
                    t.Name,
                    check = c == null ? null : new
                    {
                        c.Id,
                        c.OpenedAt,
                        c.Guests,
                        waiter = waiterNames.GetValueOrDefault(c.WaiterId ?? 0),
                        isMine = c.WaiterId == me,
                        total = co!.Sum(o => o.Total),
                        // Блюда готовы — пора нести
                        ready = co!.Count(o => o.Status == OrderStatuses.Ready),
                        cooking = co!.Count(o => o.Status is OrderStatuses.New or OrderStatuses.Accepted or OrderStatuses.Preparing)
                    }
                };
            }));
        }

        // GET: api/waiter/tables/all — все столы для настройки
        [HttpGet("tables/all")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> AllTables() =>
            Ok((await TablesAsync(onlyActive: false)).Select(t => new { t.Id, t.Name, t.IsActive }));

        // PUT: api/waiter/tables — список столов целиком (порядок — как в списке)
        [HttpPut("tables")]
        [HasPermission(Permissions.Settings)]
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
            var existing = await _context.TableChecks.FirstOrDefaultAsync(c => c.TableId == r.TableId && c.Status == CheckStatuses.Open);
            if (existing != null) return Ok(new { existing.Id, alreadyOpen = true });

            var check = new TableCheck
            {
                TableId = table.Id,
                WaiterId = User.GetUserId(),
                Guests = r.Guests,
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
            var orders = await _context.Orders.AsNoTracking()
                .Where(o => o.CheckId == id)
                .Include(o => o.OrderItems).ThenInclude(i => i.SelectedAddons)
                .OrderBy(o => o.CreatedAt)
                .ToListAsync();
            var itemIds = orders.SelectMany(o => o.OrderItems).Select(i => i.Id).ToList();
            var states = await _context.OrderItemKitchenStates.AsNoTracking().Where(x => itemIds.Contains(x.OrderItemId)).ToDictionaryAsync(x => x.OrderItemId);
            var active = orders.Where(o => o.Status != OrderStatuses.Cancelled).ToList();

            return Ok(new
            {
                c.Id,
                c.Status,
                table = new { c.Table!.Id, c.Table.Name },
                waiter,
                isMine = c.WaiterId == User.GetUserId(),
                c.Guests,
                c.Comment,
                c.OpenedAt,
                c.ClosedAt,
                c.PaymentMethod,
                total = c.Status == CheckStatuses.Closed ? c.Total : active.Sum(o => o.Total),
                // Всё ли отдано гостям (иначе при закрытии предупреждаем)
                notServed = active.Where(o => o.Status != OrderStatuses.Completed)
                    .SelectMany(o => o.OrderItems).Sum(i => i.Quantity - Math.Min(i.Quantity, states.GetValueOrDefault(i.Id)?.ServedQuantity ?? 0)),
                orders = orders.Select(o => new
                {
                    o.Id,
                    o.Status,
                    o.Total,
                    o.Notes,
                    o.CreatedAt,
                    items = o.OrderItems.OrderBy(i => i.Id).Select(i => new
                    {
                        i.Id,
                        i.Name,
                        i.Quantity,
                        i.Price,
                        addons = i.SelectedAddons.Select(a => new { a.AddonName, a.Quantity, a.Price }),
                        readyQuantity = o.Status is OrderStatuses.Ready or OrderStatuses.Completed
                            ? i.Quantity : Math.Min(i.Quantity, states.GetValueOrDefault(i.Id)?.ReadyQuantity ?? 0),
                        servedQuantity = o.Status == OrderStatuses.Completed
                            ? i.Quantity : Math.Min(i.Quantity, states.GetValueOrDefault(i.Id)?.ServedQuantity ?? 0)
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

        // POST: api/waiter/checks/{id}/orders/{orderId}/cancel — отменить отправку, пока кухня не начала готовить
        [HttpPost("checks/{id:int}/orders/{orderId:int}/cancel")]
        [HasPermission(Permissions.Waiter)]
        public async Task<IActionResult> CancelRound(int id, int orderId)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.CheckId == id);
            if (order == null) return NotFound(new { message = "Заказ не найден" });
            if (!OrderStatuses.IsNotStarted(order.Status)) return BadRequest(new { message = "Кухня уже готовит — отменить может только кухня или менеджер" });
            await _statusService.ChangeStatusAsync(order, OrderStatuses.Cancelled);
            return NoContent();
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

            var orders = await _context.Orders.Where(o => o.CheckId == id && o.Status != OrderStatuses.Cancelled).ToListAsync();
            if (orders.Count == 0) return BadRequest(new { message = "В чеке нет заказов — его можно просто отменить" });
            var total = orders.Sum(o => o.Total);
            if (method == "cash" && r.CashReceived.HasValue && r.CashReceived.Value < total)
                return BadRequest(new { message = $"Получено меньше суммы чека ({total:0.##} ₽)" });

            var now = DateTime.UtcNow;
            foreach (var o in orders)
            {
                o.PaymentMethod = method;
                o.PaymentStatus = "paid";
                o.PaidAt = now;
            }
            c.Status = CheckStatuses.Closed;
            c.ClosedAt = now;
            c.PaymentMethod = method;
            c.Total = total;
            await _context.SaveChangesAsync();
            foreach (var o in orders)
                _events.Publish(new OrderEvent("updated", o.Id, o.UserId, o.Status, null, now));
            _logger.LogInformation("🍽 Чек #{CheckId} закрыт: {Total} ₽ ({Method})", c.Id, total, method);
            return Ok(new
            {
                c.Id,
                total,
                change = method == "cash" && r.CashReceived.HasValue ? r.CashReceived.Value - total : (decimal?)null,
                orderIds = orders.Select(o => o.Id)
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
            if (await _context.Orders.AnyAsync(o => o.CheckId == id && o.Status != OrderStatuses.Cancelled))
                return BadRequest(new { message = "В чеке есть заказы — отмените их или закройте чек оплатой" });
            c.Status = CheckStatuses.Cancelled;
            c.ClosedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return NoContent();
        }

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
