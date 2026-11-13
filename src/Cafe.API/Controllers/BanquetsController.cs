using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Authorization;
using Cafe.API.Data;
using Cafe.API.Extensions;
using Cafe.API.Models;
using Cafe.API.Services;
using Cafe.API.Services.Fiscal;
using Cafe.API.Services.Printing;

namespace Cafe.API.Controllers
{
    /// <summary>
    /// Банкеты: заказ на дату; блюда уходят на кухню в «отправить на кухню» (или кнопкой);
    /// обслуживание, скидка, предоплаты (чек «аванс») и расчёт при закрытии (зачёт аванса + остаток).
    /// </summary>
    [ApiController]
    [Route("api/banquets")]
    [HasPermission(Permissions.Banquets)]
    public class BanquetsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly BanquetService _banquets;
        private readonly FiscalService _fiscal;
        private readonly FiscalSettingsProvider _fiscalSettings;
        private readonly PrintService _print;
        private readonly BrandSettingsProvider _brand;
        private readonly VoidService _voids;
        private readonly ILogger<BanquetsController> _logger;

        public BanquetsController(ApplicationDbContext context, BanquetService banquets, FiscalService fiscal, FiscalSettingsProvider fiscalSettings,
            PrintService print, BrandSettingsProvider brand, VoidService voids, ILogger<BanquetsController> logger)
        {
            _context = context;
            _banquets = banquets;
            _fiscal = fiscal;
            _fiscalSettings = fiscalSettings;
            _print = print;
            _brand = brand;
            _voids = voids;
            _logger = logger;
        }

        public class BanquetRequest
        {
            public string CustomerName { get; set; } = string.Empty;
            public string? Phone { get; set; }
            public DateTime EventAt { get; set; }
            public int? Guests { get; set; }
            public int? TableId { get; set; }
            public string? Comment { get; set; }
            /// <summary>Когда отправить блюда на кухню; пусто — не отправлять автоматически</summary>
            public DateTime? KitchenAt { get; set; }
            /// <summary>percent или amount; пусто — без обслуживания</summary>
            public string? ServiceType { get; set; }
            public decimal? ServiceValue { get; set; }
        }

        public class ItemsRequest
        {
            public List<PosController.PosItemRequest> Items { get; set; } = new();
        }

        public class QuantityRequest
        {
            public int Quantity { get; set; }
        }

        public class PrepayRequest
        {
            public decimal Amount { get; set; }
            /// <summary>cash или card</summary>
            public string Method { get; set; } = "cash";
        }

        public class CloseRequest
        {
            /// <summary>Остаток: cash, card или staff (на сотрудника — без оплаты)</summary>
            public string Method { get; set; } = "cash";
            public int? StaffUserId { get; set; }
            public string? StaffReason { get; set; }
        }

        public class CancelRequest
        {
            public string Reason { get; set; } = string.Empty;
        }

        private static DateTime Utc(DateTime d) => d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : DateTime.SpecifyKind(d, DateTimeKind.Utc);

        private static string? Validate(BanquetRequest r)
        {
            if (string.IsNullOrWhiteSpace(r.CustomerName)) return "Укажите, на кого банкет";
            if (r.EventAt == default) return "Укажите дату и время банкета";
            if (r.Guests is < 0 or > 10000) return "Проверьте число гостей";
            if (!string.IsNullOrEmpty(r.ServiceType) && r.ServiceType is not (DiscountTypes.Percent or DiscountTypes.Amount)) return "Обслуживание — процентом или суммой";
            if (r.ServiceValue is < 0) return "Обслуживание не может быть отрицательным";
            if (r.ServiceType == DiscountTypes.Percent && r.ServiceValue > 100) return "Обслуживание — не больше 100%";
            return null;
        }

        private static void Apply(Banquet b, BanquetRequest r)
        {
            b.CustomerName = r.CustomerName.Trim().Length > 120 ? r.CustomerName.Trim()[..120] : r.CustomerName.Trim();
            b.Phone = string.IsNullOrWhiteSpace(r.Phone) ? null : r.Phone.Trim();
            b.EventAt = Utc(r.EventAt);
            b.Guests = r.Guests;
            b.TableId = r.TableId;
            b.Comment = string.IsNullOrWhiteSpace(r.Comment) ? null : r.Comment.Trim().Length > 1000 ? r.Comment.Trim()[..1000] : r.Comment.Trim();
            b.KitchenAt = r.KitchenAt == null ? null : Utc(r.KitchenAt.Value);
            var noService = string.IsNullOrEmpty(r.ServiceType) || r.ServiceValue is null or 0;
            b.ServiceType = noService ? null : r.ServiceType;
            b.ServiceValue = noService ? null : Math.Round(r.ServiceValue!.Value, 2);
        }

        private async Task<Banquet?> FindAsync(int id) => await _context.Banquets.FirstOrDefaultAsync(b => b.Id == id);

        // GET: api/banquets?from=2026-11-01&to=2026-12-31 — банкеты по дате проведения (и все открытые)
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
            var f = (from ?? today.AddDays(-7)).ToDateTime(TimeOnly.MinValue).AddHours(-3);
            var t = (to ?? today.AddDays(90)).ToDateTime(TimeOnly.MinValue).AddDays(1).AddHours(-3);
            var list = await _context.Banquets.AsNoTracking()
                .Where(b => (b.EventAt >= f && b.EventAt < t) || b.Status == BanquetStatuses.Open)
                .OrderBy(b => b.EventAt).Take(500).ToListAsync();
            var ids = list.Select(b => b.Id).ToList();
            var items = await _context.BanquetItems.AsNoTracking().Where(i => ids.Contains(i.BanquetId)).ToListAsync();
            var payments = await _context.BanquetPayments.AsNoTracking().Where(p => ids.Contains(p.BanquetId)).ToListAsync();
            var tables = await _context.DiningTables.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name);
            return Ok(list.Select(b =>
            {
                var mine = items.Where(i => i.BanquetId == b.Id).ToList();
                var tt = BanquetService.Totals(b, mine, payments.Where(p => p.BanquetId == b.Id));
                return new
                {
                    b.Id, b.CustomerName, b.Phone, b.EventAt, b.Guests, b.Status, b.KitchenAt,
                    table = b.TableId == null ? null : tables.GetValueOrDefault(b.TableId.Value),
                    total = b.Status == BanquetStatuses.Closed ? b.Total : tt.Total,
                    tt.Prepaid,
                    dishes = mine.Sum(i => i.Quantity),
                    unsent = mine.Where(i => i.OrderId == null).Sum(i => i.Quantity),
                };
            }));
        }

        // GET: api/banquets/{id}
        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id)
        {
            var b = await _context.Banquets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (b == null) return NotFound(new { message = "Банкет не найден" });
            var items = await _context.BanquetItems.AsNoTracking().Where(i => i.BanquetId == id).OrderBy(i => i.Id).ToListAsync();
            var payments = await _context.BanquetPayments.AsNoTracking().Where(p => p.BanquetId == id).OrderBy(p => p.Id).ToListAsync();
            var receipts = await _context.FiscalReceipts.AsNoTracking().Where(r => r.BanquetId == id).OrderBy(r => r.Id).ToListAsync();
            var orderIds = items.Where(i => i.OrderId != null).Select(i => i.OrderId!.Value).Distinct().ToList();
            var orderStatus = await _context.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Status);
            var names = await WaiterController.StaffNamesAsync(_context,
                payments.Select(p => p.CreatedBy).Concat(payments.Select(p => p.StaffUserId)).Append(b.CreatedBy).Append(b.ClosedBy).Append(b.DiscountBy));
            var t = BanquetService.Totals(b, items, payments);
            var fo = await _fiscalSettings.GetAsync();
            var table = b.TableId == null ? null : await _context.DiningTables.AsNoTracking().Where(x => x.Id == b.TableId).Select(x => x.Name).FirstOrDefaultAsync();

            return Ok(new
            {
                b.Id, b.CustomerName, b.Phone, b.EventAt, b.Guests, b.TableId, table, b.Comment, b.Status, b.KitchenAt,
                b.ServiceType, b.ServiceValue, b.DiscountType, b.DiscountValue,
                discountBy = b.DiscountBy == null ? null : names.GetValueOrDefault(b.DiscountBy.Value),
                b.CreatedAt, createdBy = b.CreatedBy == null ? null : names.GetValueOrDefault(b.CreatedBy.Value),
                b.ClosedAt, closedBy = b.ClosedBy == null ? null : names.GetValueOrDefault(b.ClosedBy.Value),
                totals = t,
                items = items.Select(i => new
                {
                    i.Id, i.DishId, i.Name, i.Price, i.Quantity, i.Weight, i.Comment,
                    addons = BanquetService.AddonsOf(i),
                    sent = i.OrderId != null,
                    kitchenStatus = i.OrderId == null ? null : orderStatus.GetValueOrDefault(i.OrderId.Value),
                }),
                payments = payments.Select(p =>
                {
                    var mine = receipts.Where(r => r.BanquetPaymentId == p.Id).ToList();
                    var last = mine.LastOrDefault();
                    return new
                    {
                        p.Id, p.Kind, p.Method, p.Amount, p.CreatedAt, p.RefundedAt, p.StaffReason,
                        staffName = p.StaffUserId == null ? null : names.GetValueOrDefault(p.StaffUserId.Value),
                        createdBy = p.CreatedBy == null ? null : names.GetValueOrDefault(p.CreatedBy.Value),
                        fiscal = last == null ? null : new { last.Id, last.Kind, last.Status, last.Error, last.FiscalDocumentNumber, sold = FiscalService.IsSold(mine) },
                    };
                }),
                canDiscount = b.Status == BanquetStatuses.Open && User.HasPermission(Permissions.Discounts),
                staffAllowed = fo.StaffChecks,
                staff = fo.StaffChecks && b.Status == BanquetStatuses.Open
                    ? (await WaiterController.StaffListAsync(_context)).OrderBy(x => x.Value).Select(x => new { id = x.Key, name = x.Value }).ToList()
                    : null,
                me = User.GetUserId(),
                fiscalEnabled = fo.Enabled,
                printEnabled = (await _print.SettingsAsync()).Enabled,
            });
        }

        // POST: api/banquets
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] BanquetRequest r)
        {
            if (Validate(r) is { } err) return BadRequest(new { message = err });
            var b = new Banquet { CreatedBy = User.GetUserId(), CreatedAt = DateTime.UtcNow };
            Apply(b, r);
            _context.Banquets.Add(b);
            await _context.SaveChangesAsync();
            _logger.LogInformation("🎉 Банкет #{Id}: {Name} на {At}", b.Id, b.CustomerName, b.EventAt);
            return Ok(new { b.Id });
        }

        // PUT: api/banquets/{id} — данные банкета, время отправки на кухню, обслуживание
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] BanquetRequest r)
        {
            if (Validate(r) is { } err) return BadRequest(new { message = err });
            var b = await FindAsync(id);
            if (b == null) return NotFound(new { message = "Банкет не найден" });
            if (b.Status != BanquetStatuses.Open) return BadRequest(new { message = "Банкет уже закрыт" });
            Apply(b, r);
            await _context.SaveChangesAsync();
            return Ok(new { b.Id });
        }

        // PUT: api/banquets/{id}/discount — скидка (право «Скидки»)
        [HttpPut("{id:int}/discount")]
        [HasPermission(Permissions.Discounts)]
        public async Task<IActionResult> Discount(int id, [FromBody] WaiterController.DiscountRequest r)
        {
            if (WaiterController.ValidateDiscount(r) is { } err) return BadRequest(new { message = err });
            var b = await FindAsync(id);
            if (b == null) return NotFound(new { message = "Банкет не найден" });
            if (b.Status != BanquetStatuses.Open) return BadRequest(new { message = "Банкет уже закрыт" });
            var off = r.Value is null or 0 || string.IsNullOrEmpty(r.Type);
            b.DiscountType = off ? null : r.Type;
            b.DiscountValue = off ? null : Math.Round(r.Value!.Value, 2);
            b.DiscountBy = off ? null : User.GetUserId();
            await _context.SaveChangesAsync();
            return Ok(new { b.Id });
        }

        // POST: api/banquets/{id}/items — добавить блюда (на кухню не уходят до «отправить на кухню»)
        [HttpPost("{id:int}/items")]
        public async Task<IActionResult> AddItems(int id, [FromBody] ItemsRequest r)
        {
            var b = await FindAsync(id);
            if (b == null) return NotFound(new { message = "Банкет не найден" });
            if (b.Status != BanquetStatuses.Open) return BadRequest(new { message = "Банкет уже закрыт" });
            if (r.Items.Count == 0) return BadRequest(new { message = "Добавьте блюда" });
            if (r.Items.Count > 200 || r.Items.Any(i => i.Quantity <= 0 || i.Quantity > 999 || i.Addons.Any(a => a.Quantity <= 0 || a.Quantity > 50)))
                return BadRequest(new { message = "Проверьте количество" });
            var (built, _, error) = await PosController.BuildItemsAsync(_context, r.Items, SalesChannels.DineIn);
            if (error != null) return BadRequest(new { message = error });
            for (var k = 0; k < built!.Count; k++)
            {
                var oi = built[k];
                _context.BanquetItems.Add(new BanquetItem
                {
                    BanquetId = id,
                    DishId = oi.DishId,
                    Name = oi.Name.Length > 200 ? oi.Name[..200] : oi.Name,
                    Price = VoidService.UnitPrice(oi),
                    Quantity = oi.Quantity,
                    Weight = oi.Weight,
                    Comment = k < r.Items.Count && !string.IsNullOrWhiteSpace(r.Items[k].Comment) ? r.Items[k].Comment!.Trim() : null,
                    Addons = oi.SelectedAddons.Count == 0 ? null
                        : BanquetService.AddonsJson(oi.SelectedAddons.Select(a => new BanquetAddon(a.AddonId, a.Quantity, a.AddonName, a.Price))),
                });
            }
            await _context.SaveChangesAsync();
            return Ok(new { added = built.Count });
        }

        // PUT: api/banquets/{id}/items/{itemId} — количество (пока блюдо не отправлено на кухню)
        [HttpPut("{id:int}/items/{itemId:int}")]
        public async Task<IActionResult> SetQuantity(int id, int itemId, [FromBody] QuantityRequest r)
        {
            var i = await _context.BanquetItems.FirstOrDefaultAsync(x => x.Id == itemId && x.BanquetId == id);
            if (i == null) return NotFound(new { message = "Блюдо не найдено" });
            if (i.OrderId != null) return BadRequest(new { message = "Блюдо уже на кухне — его меняют через отмену банкета" });
            if (r.Quantity is < 1 or > 999) return BadRequest(new { message = "Проверьте количество" });
            i.Quantity = r.Quantity;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/banquets/{id}/items/{itemId} — убрать (пока не на кухне)
        [HttpDelete("{id:int}/items/{itemId:int}")]
        public async Task<IActionResult> RemoveItem(int id, int itemId)
        {
            var i = await _context.BanquetItems.FirstOrDefaultAsync(x => x.Id == itemId && x.BanquetId == id);
            if (i == null) return NotFound(new { message = "Блюдо не найдено" });
            if (i.OrderId != null) return BadRequest(new { message = "Блюдо уже на кухне" });
            _context.BanquetItems.Remove(i);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // POST: api/banquets/{id}/kitchen — отправить на кухню сейчас (не отправленные блюда)
        [HttpPost("{id:int}/kitchen")]
        public async Task<IActionResult> SendNow(int id)
        {
            try
            {
                var order = await _banquets.SendToKitchenAsync(id, User.GetUserId());
                if (order == null) return BadRequest(new { message = "Все блюда уже на кухне" });
                return Ok(new { orderId = order.Id });
            }
            catch (BanquetService.BanquetException ex) { return BadRequest(new { message = ex.Message }); }
        }

        // POST: api/banquets/{id}/print — пречек (сколько угодно раз)
        [HttpPost("{id:int}/print")]
        public async Task<IActionResult> Print(int id)
        {
            var b = await _context.Banquets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (b == null) return NotFound(new { message = "Банкет не найден" });
            var settings = await _print.SettingsAsync();
            if (!settings.Enabled) return BadRequest(new { message = "Принтер счетов не настроен — включите его в «Настройке интеграций» → «Принтер счетов»" });
            var items = await _context.BanquetItems.AsNoTracking().Where(i => i.BanquetId == id).OrderBy(i => i.Id).ToListAsync();
            if (items.Count == 0) return BadRequest(new { message = "В банкете нет блюд" });
            var payments = await _context.BanquetPayments.AsNoTracking().Where(p => p.BanquetId == id).ToListAsync();
            var t = BanquetService.Totals(b, items, payments);
            var lines = items.Select(i =>
            {
                var addons = BanquetService.AddonsOf(i);
                var name = addons.Count > 0 ? $"{i.Name} + {string.Join(", ", addons.Select(a => a.Name))}" : i.Name;
                return new PrecheckLine(name, i.Quantity, i.Price, i.Price * i.Quantity);
            }).ToList();
            var adj = new List<PrecheckAdjustment>();
            if (t.Discount > 0) adj.Add(new PrecheckAdjustment(WaiterController.DiscountLabel(b.DiscountType, b.DiscountValue), -t.Discount));
            if (t.Service > 0) adj.Add(new PrecheckAdjustment(b.ServiceType == DiscountTypes.Percent ? $"Обслуживание {b.ServiceValue:0.##}%" : "Обслуживание", t.Service));
            if (t.Prepaid > 0) adj.Add(new PrecheckAdjustment("Предоплата", -t.Prepaid, IsPrepaid: true));
            var table = b.TableId == null ? "" : await _context.DiningTables.AsNoTracking().Where(x => x.Id == b.TableId).Select(x => x.Name).FirstOrDefaultAsync() ?? "";
            var info = new List<string> { $"{b.EventAt.AddHours(3):dd.MM.yyyy HH:mm}" };
            if (!string.IsNullOrWhiteSpace(b.Phone)) info.Add(b.Phone!);
            var data = new PrecheckData((await _brand.GetAsync()).DisplayName, table, $"№{b.Id} · {b.CustomerName}", null, b.Guests,
                b.CreatedAt, DateTime.UtcNow, lines, settings.Footer, Title: "БАНКЕТ", Adjustments: adj, Info: info);
            var job = await _print.EnqueueAsync("precheck", null, $"Банкет №{b.Id}", PrintService.Precheck(settings, data), User.GetUserId());
            return Ok(new { jobId = job.Id });
        }

        private async Task<object?> FiscalSellAsync(Banquet b, BanquetPayment p, List<ReceiptLine> lines, string method, IReadOnlyList<(string, decimal)>? payments)
        {
            if (!await _fiscal.EnabledAsync()) return null;
            try { return FiscalController.View(await _fiscal.PrintBanquetAsync(b.Id, p.Id, FiscalKinds.Sell, lines, method, payments, b.Phone, User.GetUserId(), HttpContext.RequestAborted)); }
            catch (FiscalException ex) { return new { status = FiscalStatuses.Error, error = ex.Message }; }
        }

        // POST: api/banquets/{id}/prepay — предоплата: свой чек «аванс»
        [HttpPost("{id:int}/prepay")]
        public async Task<IActionResult> Prepay(int id, [FromBody] PrepayRequest r)
        {
            var b = await FindAsync(id);
            if (b == null) return NotFound(new { message = "Банкет не найден" });
            if (b.Status != BanquetStatuses.Open) return BadRequest(new { message = "Банкет уже закрыт" });
            var method = r.Method?.Trim().ToLowerInvariant();
            if (method is not (CheckPayments.Cash or CheckPayments.Card)) return BadRequest(new { message = "Предоплата — наличными или картой" });
            if (r.Amount <= 0 || r.Amount > 10_000_000) return BadRequest(new { message = "Проверьте сумму" });
            var p = new BanquetPayment
            {
                BanquetId = id, Kind = BanquetPaymentKinds.Prepay, Method = method!, Amount = Math.Round(r.Amount, 2),
                CreatedAt = DateTime.UtcNow, CreatedBy = User.GetUserId(),
            };
            _context.BanquetPayments.Add(p);
            await _context.SaveChangesAsync();
            _logger.LogInformation("🎉 Банкет #{Id}: предоплата {Amount} ₽ ({Method})", id, p.Amount, method);
            // Предоплата только записывается — чек на всю сумму уйдёт при закрытии банкета
            return Ok(new { paymentId = p.Id, fiscal = (object?)null, fiscalSkipped = "recorded" });
        }

        // POST: api/banquets/{id}/payments/{paymentId}/refund — вернуть предоплату (чек возврата аванса)
        [HttpPost("{id:int}/payments/{paymentId:int}/refund")]
        public async Task<IActionResult> Refund(int id, int paymentId)
        {
            var b = await FindAsync(id);
            if (b == null) return NotFound(new { message = "Банкет не найден" });
            if (b.Status != BanquetStatuses.Open) return BadRequest(new { message = "Банкет уже закрыт" });
            var p = await _context.BanquetPayments.FirstOrDefaultAsync(x => x.Id == paymentId && x.BanquetId == id);
            if (p == null || p.Kind != BanquetPaymentKinds.Prepay) return NotFound(new { message = "Предоплата не найдена" });
            var (ok, error, ret) = await RefundAsync(b, p);
            if (!ok) return BadRequest(new { message = error });
            return Ok(new { returned = ret });
        }

        /// <summary>Вернуть предоплату: если по ней пробит чек — сначала чек возврата</summary>
        private async Task<(bool Ok, string? Error, object? Returned)> RefundAsync(Banquet b, BanquetPayment p)
        {
            if (p.RefundedAt != null) return (true, null, null);
            var receipts = await _context.FiscalReceipts.AsNoTracking().Where(r => r.BanquetPaymentId == p.Id).OrderBy(r => r.Id).ToListAsync();
            if (receipts.LastOrDefault()?.Status == FiscalStatuses.Processing)
                return (false, "Касса ещё не ответила по чеку предоплаты — сначала проверьте его", null);
            object? view = null;
            if (FiscalService.IsSold(receipts))
            {
                FiscalReceipt ret;
                try { ret = await _fiscal.PrintBanquetAsync(b.Id, p.Id, FiscalKinds.Return, new List<ReceiptLine>(), p.Method, null, b.Phone, User.GetUserId(), HttpContext.RequestAborted); }
                catch (FiscalException ex) { return (false, $"Чек возврата не пробит: {ex.Message}", null); }
                if (ret.Status != FiscalStatuses.Done) return (false, $"Чек возврата не пробит: {ret.Error ?? "касса не ответила"}", FiscalController.View(ret));
                view = FiscalController.View(ret);
            }
            p.RefundedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            _logger.LogInformation("🎉 Банкет #{Id}: предоплата {Amount} ₽ возвращена", b.Id, p.Amount);
            return (true, null, view);
        }

        /// <summary>
        /// Оплаты итогового чека: предоплаты (наличными/картой — как вносили) и остаток.
        /// Предоплата, по которой уже пробит чек аванса, зачитывается (prepaid).
        /// </summary>
        private async Task<(List<(string, decimal)> Payments, decimal Sum)> FinalPaymentsAsync(int banquetId, IEnumerable<BanquetPayment> all, BanquetPayment final)
        {
            var prepays = all.Where(p => p.Kind == BanquetPaymentKinds.Prepay && p.RefundedAt == null).ToList();
            var ids = prepays.Select(p => (int?)p.Id).ToList();
            var receipts = await _context.FiscalReceipts.AsNoTracking().Where(r => ids.Contains(r.BanquetPaymentId)).ToListAsync();
            decimal cash = 0, card = 0, prepaid = 0;
            foreach (var p in prepays)
            {
                if (FiscalService.IsSold(receipts.Where(r => r.BanquetPaymentId == p.Id))) prepaid += p.Amount;
                else if (p.Method == CheckPayments.Cash) cash += p.Amount;
                else card += p.Amount;
            }
            if (final.Method == CheckPayments.Cash) cash += final.Amount;
            else if (final.Method == CheckPayments.Card) card += final.Amount;
            var list = new List<(string, decimal)>();
            if (prepaid > 0) list.Add(("prepaid", prepaid));
            if (cash > 0) list.Add(("cash", cash));
            if (card > 0) list.Add(("electronically", card));
            return (list, prepaid + cash + card);
        }

        // POST: api/banquets/{id}/close — расчёт: остаток наличными, картой или на сотрудника; чек с зачётом предоплаты
        [HttpPost("{id:int}/close")]
        public async Task<IActionResult> Close(int id, [FromBody] CloseRequest r)
        {
            var b = await FindAsync(id);
            if (b == null) return NotFound(new { message = "Банкет не найден" });
            if (b.Status != BanquetStatuses.Open) return BadRequest(new { message = "Банкет уже закрыт" });
            var items = await _context.BanquetItems.AsNoTracking().Where(i => i.BanquetId == id).OrderBy(i => i.Id).ToListAsync();
            if (items.Count == 0) return BadRequest(new { message = "В банкете нет блюд — его можно отменить" });
            if (items.Any(i => i.OrderId == null)) return BadRequest(new { message = "Есть блюда, которые не отправлены на кухню — отправьте их или уберите" });
            var payments = await _context.BanquetPayments.Where(p => p.BanquetId == id).ToListAsync();
            var t = BanquetService.Totals(b, items, payments);
            if (t.Prepaid > t.Total) return BadRequest(new { message = $"Предоплата ({t.Prepaid:0.##} ₽) больше суммы банкета ({t.Total:0.##} ₽) — верните предоплату и внесите заново нужную сумму" });
            var unsure = await _context.FiscalReceipts.AsNoTracking().Where(x => x.BanquetId == id).OrderBy(x => x.Id).ToListAsync();
            if (unsure.GroupBy(x => x.BanquetPaymentId).Any(g => g.Last().Status == FiscalStatuses.Processing))
                return BadRequest(new { message = "Касса ещё не ответила по чеку предоплаты — сначала проверьте его" });

            var method = r.Method?.Trim().ToLowerInvariant();
            if (method is not (CheckPayments.Cash or CheckPayments.Card or CheckPayments.Staff)) return BadRequest(new { message = "Выберите способ оплаты" });
            string? staffName = null, staffReason = null;

            var final = new BanquetPayment
            {
                BanquetId = id, Kind = BanquetPaymentKinds.Final,
                Method = t.Due > 0 ? method! : "prepaid",
                Amount = t.Due, StaffUserId = staffName == null ? null : r.StaffUserId, StaffReason = staffReason,
                CreatedAt = DateTime.UtcNow, CreatedBy = User.GetUserId(),
            };
            _context.BanquetPayments.Add(final);
            b.Status = BanquetStatuses.Closed;
            b.Total = t.Total;
            b.ClosedAt = DateTime.UtcNow;
            b.ClosedBy = User.GetUserId();
            foreach (var o in await _context.Orders.Where(o => o.BanquetId == id && o.Status != OrderStatuses.Cancelled).ToListAsync())
            {
                o.PaymentStatus = "paid";
                o.PaidAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();
            _logger.LogInformation("🎉 Банкет #{Id} закрыт: {Total} ₽, предоплата {Prepaid}, остаток {Due} ({Method})", id, t.Total, t.Prepaid, t.Due, final.Method);

            // Один чек на всё, что заплатили деньгами: предоплаты и остаток (часть на сотрудника — без чека)
            var (pays, fiscalSum) = await FinalPaymentsAsync(id, payments, final);
            object? fiscal = null;
            string? skipped = null;
            if (fiscalSum <= 0) skipped = "staff";
            else
            {
                fiscal = await FiscalSellAsync(b, final, BanquetService.FinalLines(items, t, fiscalSum), pays.Any(x => x.Item1 == "cash") ? "cash" : "card", pays);
                if (fiscal == null) skipped = "disabled";
            }
            return Ok(new { t.Total, t.Prepaid, t.Due, fiscal, fiscalSkipped = skipped, staff = staffName });
        }

        // POST: api/banquets/receipts/{receiptId}/retry — пробить (проверить) чек банкета, который не пробился
        [HttpPost("receipts/{receiptId:int}/retry")]
        public async Task<IActionResult> Retry(int receiptId)
        {
            var rc = await _context.FiscalReceipts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == receiptId);
            if (rc?.BanquetPaymentId == null) return NotFound(new { message = "Чек не найден" });
            var p = await _context.BanquetPayments.AsNoTracking().FirstAsync(x => x.Id == rc.BanquetPaymentId);
            var b = await _context.Banquets.AsNoTracking().FirstAsync(x => x.Id == p.BanquetId);
            List<ReceiptLine> lines = new();
            IReadOnlyList<(string, decimal)>? pays = null;
            var method = p.Method;
            if (rc.Kind == FiscalKinds.Sell && p.Kind == BanquetPaymentKinds.Prepay) lines = BanquetService.PrepayLines(b, p.Amount);
            else if (rc.Kind == FiscalKinds.Sell)
            {
                var items = await _context.BanquetItems.AsNoTracking().Where(i => i.BanquetId == b.Id).OrderBy(i => i.Id).ToListAsync();
                var all = await _context.BanquetPayments.AsNoTracking().Where(x => x.BanquetId == b.Id).ToListAsync();
                var t = BanquetService.Totals(b, items, all);
                var (list, sum) = await FinalPaymentsAsync(b.Id, all, p);
                lines = BanquetService.FinalLines(items, t, sum);
                pays = list;
                method = list.Any(x => x.Item1 == "cash") ? "cash" : "card";
            }
            try { return Ok(FiscalController.View(await _fiscal.PrintBanquetAsync(b.Id, p.Id, rc.Kind, lines, method, pays, b.Phone, User.GetUserId(), HttpContext.RequestAborted))); }
            catch (FiscalException ex) { return BadRequest(new { message = ex.Message }); }
        }

        // POST: api/banquets/{id}/cancel — отменить банкет: вернуть предоплаты, отменить блюда на кухне
        [HttpPost("{id:int}/cancel")]
        public async Task<IActionResult> Cancel(int id, [FromBody] CancelRequest r)
        {
            var reason = r.Reason?.Trim() ?? "";
            if (reason.Length == 0) return BadRequest(new { message = "Укажите причину" });
            var b = await FindAsync(id);
            if (b == null) return NotFound(new { message = "Банкет не найден" });
            if (b.Status != BanquetStatuses.Open) return BadRequest(new { message = "Отменить можно открытый банкет" });

            var returned = new List<object>();
            foreach (var p in await _context.BanquetPayments.Where(x => x.BanquetId == id && x.Kind == BanquetPaymentKinds.Prepay && x.RefundedAt == null).ToListAsync())
            {
                var (ok, error, ret) = await RefundAsync(b, p);
                if (!ok) return BadRequest(new { message = $"{error}. Банкет не отменён" });
                if (ret != null) returned.Add(ret);
            }

            var itemIds = await _context.OrderItems.Where(i => i.Order!.BanquetId == id && i.Order.Status != OrderStatuses.Cancelled).Select(i => i.Id).ToListAsync();
            int done = 0, pending = 0;
            if (itemIds.Count > 0)
            {
                try { (done, pending) = await _voids.CancelAllAsync(itemIds, $"Банкет отменён: {reason}", User.GetUserId()); }
                catch (VoidService.VoidException ex) { return BadRequest(new { message = ex.Message }); }
            }
            b.Status = BanquetStatuses.Cancelled;
            b.ClosedAt = DateTime.UtcNow;
            b.ClosedBy = User.GetUserId();
            b.Comment = string.IsNullOrEmpty(b.Comment) ? $"Отменён: {reason}" : $"{b.Comment}\nОтменён: {reason}";
            if (b.Comment.Length > 1000) b.Comment = b.Comment[..1000];
            await _context.SaveChangesAsync();
            _logger.LogInformation("🎉 Банкет #{Id} отменён ({Reason}), возвратов {Returns}", id, reason, returned.Count);
            return Ok(new { returned = returned.Count, done, pending });
        }
    }
}
