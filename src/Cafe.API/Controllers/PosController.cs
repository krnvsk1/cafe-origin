using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Authorization;
using Cafe.API.Data;
using Cafe.API.Extensions;
using Cafe.API.Models;
using Cafe.API.Services;
using Cafe.API.Services.Fiscal;

namespace Cafe.API.Controllers
{
    /// <summary>
    /// Касса: заказы у стойки (в зале и с собой). Оплата принимается сразу, заказ уходит на кухню,
    /// продукты списываются по техкартам.
    /// </summary>
    [ApiController]
    [Route("api/pos")]
    [HasPermission(Permissions.Pos)]
    public class PosController : ControllerBase
    {
        public const string Source = "pos";
        private static readonly string[] OrderTypes = { "В зале", "Самовывоз" };

        private readonly ApplicationDbContext _context;
        private readonly IOrderStatusService _statusService;
        private readonly IOrderEventBus _events;
        private readonly FiscalService _fiscal;
        private readonly ILogger<PosController> _logger;

        public PosController(ApplicationDbContext context, IOrderStatusService statusService, IOrderEventBus events, FiscalService fiscal, ILogger<PosController> logger)
        {
            _context = context;
            _statusService = statusService;
            _events = events;
            _fiscal = fiscal;
            _logger = logger;
        }

        public class PosAddonRequest
        {
            public int AddonId { get; set; }
            public int Quantity { get; set; } = 1;
        }

        public class PosItemRequest
        {
            public int DishId { get; set; }
            public int Quantity { get; set; } = 1;
            public List<PosAddonRequest> Addons { get; set; } = new();
            public string? Comment { get; set; }
        }

        public class PosOrderRequest
        {
            public List<PosItemRequest> Items { get; set; } = new();
            /// <summary>«В зале» или «Самовывоз» (с собой)</summary>
            public string OrderType { get; set; } = "В зале";
            /// <summary>cash или card</summary>
            public string PaymentMethod { get; set; } = "cash";
            /// <summary>Сколько дал гость наличными — для расчёта сдачи</summary>
            public decimal? CashReceived { get; set; }
            public string? CustomerName { get; set; }
            /// <summary>Телефон гостя: если он зарегистрирован, заказ попадёт в его историю и принесёт баллы</summary>
            public string? Phone { get; set; }
            public string? Comment { get; set; }
            /// <summary>Сразу отправить на кухню (статус «Готовится»)</summary>
            public bool SendToKitchen { get; set; } = true;
        }

        // POST: api/pos/orders — принять заказ на кассе
        [HttpPost("orders")]
        public async Task<IActionResult> Create([FromBody] PosOrderRequest request)
        {
            if (request.Items.Count == 0) return BadRequest(new { message = "Добавьте позиции в чек" });
            if (request.Items.Count > 100) return BadRequest(new { message = "Слишком много позиций в одном чеке" });
            if (request.Items.Any(i => i.Quantity <= 0 || i.Quantity > 99 || i.Addons.Any(a => a.Quantity <= 0 || a.Quantity > 20)))
                return BadRequest(new { message = "Проверьте количество" });
            if (!OrderTypes.Contains(request.OrderType)) return BadRequest(new { message = "Выберите: в зале или с собой" });

            var payment = request.PaymentMethod?.Trim().ToLowerInvariant();
            if (payment is not ("cash" or "card")) return BadRequest(new { message = "Выберите способ оплаты" });

            var (built, total, buildError) = await BuildItemsAsync(_context, request.Items, request.OrderType);
            if (buildError != null) return BadRequest(new { message = buildError });

            var cashierId = User.GetUserId();
            var customerUserId = await FindCustomerAsync(request.Phone);

            await using var tx = await _context.Database.BeginTransactionAsync();

            var order = new Order
            {
                UserId = customerUserId,
                CustomerName = string.IsNullOrWhiteSpace(request.CustomerName) ? "Гость" : request.CustomerName.Trim(),
                Phone = request.Phone?.Trim() ?? string.Empty,
                Address = request.OrderType == "В зале" ? "В зале" : "Самовывоз",
                DeliveryType = request.OrderType,
                Notes = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
                Status = OrderStatuses.New,
                PaymentMethod = payment,
                PaymentStatus = "paid",
                PaidAt = DateTime.UtcNow,
                Source = Source,
                CashierId = cashierId,
                CreatedAt = DateTime.UtcNow,
                AccessToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()
            };

            foreach (var orderItem in built!) order.OrderItems.Add(orderItem);

            order.Total = total;
            if (payment == "cash" && request.CashReceived.HasValue && request.CashReceived.Value < total)
                return BadRequest(new { message = $"Получено меньше суммы чека ({total:0.##} ₽)" });

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            _events.Publish(new OrderEvent("created", order.Id, order.UserId, order.Status, null, DateTime.UtcNow));

            // Сразу в работу: кухня видит заказ, продукты списываются по техкартам
            if (request.SendToKitchen)
                await _statusService.ChangeStatusAsync(order, OrderStatuses.Preparing);

            _logger.LogInformation("🧾 Касса: заказ #{OrderId} на {Total} ₽ ({Payment}), кассир {CashierId}", order.Id, order.Total, payment, cashierId);

            // Чек на кассе АТОЛ. Если касса недоступна — заказ всё равно принят, чек можно повторить из списка заказов
            object? fiscal = null;
            if (order.Total > 0 && await _fiscal.EnabledAsync())
            {
                try { fiscal = FiscalController.View(await _fiscal.PrintAsync(order.Id, Models.FiscalKinds.Sell, cashierId, HttpContext.RequestAborted)); }
                catch (FiscalException ex) { fiscal = new { status = Models.FiscalStatuses.Error, error = ex.Message }; }
            }

            return Ok(new
            {
                fiscal,
                order.Id,
                order.Total,
                order.Status,
                order.PaymentMethod,
                change = payment == "cash" && request.CashReceived.HasValue ? request.CashReceived.Value - total : (decimal?)null,
                customerFound = customerUserId.HasValue
            });
        }

        // GET: api/pos/orders?all=false — заказы за сегодня (по умолчанию — принятые на кассе)
        [HttpGet("orders")]
        public async Task<IActionResult> Today([FromQuery] bool all = false)
        {
            var since = StartOfTodayUtc();
            var query = _context.Orders.AsNoTracking().Where(o => o.CreatedAt >= since);
            if (!all) query = query.Where(o => o.Source == Source);

            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .Take(200)
                .Select(o => new
                {
                    o.Id,
                    o.Status,
                    o.Total,
                    o.CustomerName,
                    o.Phone,
                    o.DeliveryType,
                    o.PaymentMethod,
                    o.PaymentStatus,
                    o.Source,
                    o.Notes,
                    o.CreatedAt,
                    o.CompletedAt,
                    fiscal = _context.FiscalReceipts.Where(r => r.OrderId == o.Id).OrderByDescending(r => r.Id)
                        .Select(r => new { r.Kind, r.Status, r.Error, r.FiscalDocumentNumber }).FirstOrDefault(),
                    sold = _context.FiscalReceipts.Any(r => r.OrderId == o.Id && r.Kind == FiscalKinds.Sell && r.Status == FiscalStatuses.Done),
                    items = o.OrderItems.Select(i => new
                    {
                        i.Name,
                        i.Quantity,
                        i.Price,
                        addons = i.SelectedAddons.Select(a => new { a.AddonName, a.Quantity, a.Price })
                    })
                })
                .ToListAsync();
            return Ok(orders);
        }

        // GET: api/pos/summary — итоги смены за сегодня по кассе
        [HttpGet("summary")]
        public async Task<IActionResult> Summary()
        {
            var since = StartOfTodayUtc();
            var rows = await _context.Orders.AsNoTracking()
                .Where(o => o.Source == Source && o.CreatedAt >= since && o.Status != OrderStatuses.Cancelled)
                .GroupBy(o => o.PaymentMethod)
                .Select(g => new { Method = g.Key, Count = g.Count(), Sum = g.Sum(o => o.Total) })
                .ToListAsync();

            return Ok(new
            {
                count = rows.Sum(r => r.Count),
                total = rows.Sum(r => r.Sum),
                cash = rows.Where(r => r.Method == "cash").Sum(r => r.Sum),
                card = rows.Where(r => r.Method == "card").Sum(r => r.Sum),
                averageCheck = rows.Sum(r => r.Count) > 0 ? Math.Round(rows.Sum(r => r.Sum) / rows.Sum(r => r.Count), 0) : 0
            });
        }

        // GET: api/pos/customer?phone= — найти гостя по телефону (имя и баллы)
        [HttpGet("customer")]
        public async Task<IActionResult> Customer([FromQuery] string phone)
        {
            var id = await FindCustomerAsync(phone);
            if (id == null) return Ok(new { found = false });
            var user = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == id);
            return Ok(new { found = true, name = string.Join(' ', new[] { user.FirstName, user.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))), points = user.PointsBalance });
        }

        private async Task<int?> FindCustomerAsync(string? phone)
        {
            var normalized = NormalizePhone(phone);
            if (normalized == null) return null;
            return await _context.Users.AsNoTracking()
                .Where(u => u.PhoneNumber == normalized)
                .Select(u => (int?)u.Id)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Позиции заказа с ценами из меню (вариант, добавки со своей ценой для блюда) и проверками:
        /// стоп-лист, выбран вариант, можно продать этим способом. Общая для кассы и официанта.
        /// </summary>
        public static async Task<(List<OrderItem>? Items, decimal Total, string? Error)> BuildItemsAsync(
            ApplicationDbContext ctx, List<PosItemRequest> items, string orderType)
        {
            // Позиции: только продаваемые (вариант или карточка без вариантов) и не в стоп-листе
            var ids = items.Select(i => i.DishId).Distinct().ToList();
            var dishes = await ctx.Dishes.AsNoTracking()
                .Include(s => s.Parent)
                .Where(s => ids.Contains(s.Id))
                .Select(s => new
                {
                    s.Id,
                    s.Name,
                    s.Price,
                    s.IsAvailable,
                    s.ParentId,
                    ParentName = s.Parent != null ? s.Parent.Name : null,
                    ParentAvailable = s.Parent == null || s.Parent.IsAvailable,
                    HasChildren = s.Children.Any(),
                    s.OrderTypes,
                    ParentTypes = s.Parent != null ? s.Parent.OrderTypes : null
                })
                .ToDictionaryAsync(s => s.Id);

            foreach (var id in ids)
            {
                if (!dishes.TryGetValue(id, out var d)) return (null, 0, "Позиция не найдена — обновите меню");
                if (!d.IsAvailable || !d.ParentAvailable) return (null, 0, $"«{d.ParentName ?? d.Name}» сейчас в стоп-листе");
                if (d.HasChildren) return (null, 0, $"Выберите вариант для «{d.Name}»");
                if (!SalesChannels.Allows(d.OrderTypes, d.ParentTypes, orderType))
                    return (null, 0, $"«{d.ParentName ?? d.Name}» нельзя продать {(orderType == SalesChannels.DineIn ? "в зале" : "с собой")}");
            }

            // Цены добавок — как в карточке товара: своя цена для блюда или базовая
            var addonIds = items.SelectMany(i => i.Addons).Select(a => a.AddonId).Distinct().ToList();
            var addons = await ctx.Addons.AsNoTracking().Include(a => a.Category)
                .Where(a => addonIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id);
            var lookupIds = ids.Concat(dishes.Values.Where(d => d.ParentId.HasValue).Select(d => d.ParentId!.Value)).Distinct().ToList();
            var links = await ctx.DishAddons.AsNoTracking()
                .Where(sa => lookupIds.Contains(sa.DishId) && addonIds.Contains(sa.AddonId))
                .ToListAsync();

            if (addonIds.Any(id => !addons.TryGetValue(id, out var a) || !a.IsAvailable))
                return (null, 0, "Одна из добавок сейчас недоступна");

            var result = new List<OrderItem>();
            decimal total = 0;
            foreach (var item in items)
            {
                var dish = dishes[item.DishId];
                var name = dish.ParentName != null && dish.Name != "Стандарт" ? $"{dish.ParentName} {dish.Name}" : dish.ParentName ?? dish.Name;
                var orderItem = new OrderItem
                {
                    DishId = dish.Id,
                    Name = Truncate(string.IsNullOrWhiteSpace(item.Comment) ? name : $"{name} ({item.Comment.Trim()})", 100),
                    Quantity = item.Quantity,
                    Price = dish.Price
                };

                decimal addonsSum = 0;
                foreach (var a in item.Addons)
                {
                    var addon = addons[a.AddonId];
                    var link = links.FirstOrDefault(l => l.DishId == dish.Id && l.AddonId == a.AddonId)
                               ?? (dish.ParentId.HasValue ? links.FirstOrDefault(l => l.DishId == dish.ParentId && l.AddonId == a.AddonId) : null);
                    var price = link?.CustomPrice ?? addon.Price;
                    orderItem.SelectedAddons.Add(new OrderItemAddon
                    {
                        AddonId = addon.Id,
                        AddonName = addon.Name,
                        AddonCategoryId = addon.AddonCategoryId,
                        AddonCategoryName = addon.Category?.Name ?? "Добавки",
                        Price = price,
                        Quantity = a.Quantity
                    });
                    addonsSum += price * a.Quantity;
                }

                result.Add(orderItem);
                total += (dish.Price + addonsSum) * item.Quantity;
            }

            return (result, total, null);
        }

        /// <summary>«8 (999) 123-45-67» → «+79991234567»; null, если номер не похож на телефон</summary>
        public static string? NormalizePhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return null;
            var digits = new string(phone.Where(char.IsDigit).ToArray());
            if (digits.Length == 10) digits = "7" + digits;
            if (digits.Length == 11 && digits[0] == '8') digits = "7" + digits[1..];
            return digits.Length is >= 11 and <= 15 ? "+" + digits : null;
        }

        /// <summary>Начало сегодняшнего дня по Москве (UTC+3) в UTC</summary>
        public static DateTime StartOfTodayUtc() => DateTime.UtcNow.AddHours(3).Date.AddHours(-3);

        private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
    }
}
