using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Cafe.API.Authorization;
using Cafe.API.Data;
using Cafe.API.Extensions;
using Cafe.API.Models;
using Cafe.API.Options;
using Cafe.API.Services;
using Cafe.API.Services.Fiscal;

namespace Cafe.API.Controllers
{
    /// <summary>
    /// Заказы с сайта (доставка и самовывоз): закрывает менеджер или администратор — выбирает оплату,
    /// чек уходит в АТОЛ (или счёт на сотрудника без чека); отмена всего заказа — по правилам отмены блюд.
    /// </summary>
    [ApiController]
    [Route("api/site-orders")]
    [HasPermission(Permissions.ChecksAll)]
    public class SiteOrdersController : ControllerBase
    {
        public const string Source = "online";
        private static readonly string[] Handed = { OrderStatuses.Ready, OrderStatuses.OnTheWay, OrderStatuses.Delivered, OrderStatuses.Completed };

        private readonly ApplicationDbContext _context;
        private readonly FiscalService _fiscal;
        private readonly FiscalSettingsProvider _fiscalSettings;
        private readonly IOrderStatusService _statusService;
        private readonly VoidService _voids;
        private readonly YooKassaOptions _yooKassa;
        private readonly ILogger<SiteOrdersController> _logger;

        public SiteOrdersController(ApplicationDbContext context, FiscalService fiscal, FiscalSettingsProvider fiscalSettings,
            IOrderStatusService statusService, VoidService voids, IOptions<YooKassaOptions> yooKassa, ILogger<SiteOrdersController> logger)
        {
            _context = context;
            _fiscal = fiscal;
            _fiscalSettings = fiscalSettings;
            _statusService = statusService;
            _voids = voids;
            _yooKassa = yooKassa.Value;
            _logger = logger;
        }

        public class CloseRequest
        {
            /// <summary>cash, card, staff; для оплаченных онлайн — online</summary>
            public string PaymentMethod { get; set; } = "cash";
            public int? StaffUserId { get; set; }
            public string? StaffReason { get; set; }
            /// <summary>Скидка при закрытии (право «Скидки»): percent или amount</summary>
            public string? DiscountType { get; set; }
            public decimal? DiscountValue { get; set; }
        }

        public class CancelRequest
        {
            public string Reason { get; set; } = string.Empty;
        }

        /// <summary>Онлайн-оплату фискализирует ЮKassa, если там включены чеки; иначе — наша касса</summary>
        private bool OnlineViaAtol => !_yooKassa.SendReceipt;

        // GET: api/site-orders — заказы с сайта за последние дни: ждут закрытия и закрытые сегодня
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] int days = 3)
        {
            days = Math.Clamp(days, 1, 14);
            var since = DateTime.UtcNow.AddDays(-days);
            var today = DateTime.UtcNow.AddHours(-18);
            var orders = await _context.Orders.AsNoTracking()
                .Where(o => o.Source == Source && o.CheckId == null && o.CreatedAt >= since)
                .Where(o => o.PaymentMethod != "online" || o.PaymentStatus == "paid" || o.PaymentStatus == "refunded")
                .Where(o => (o.ClosedAt == null && o.Status != OrderStatuses.Cancelled) || o.ClosedAt >= today || (o.Status == OrderStatuses.Cancelled && o.CreatedAt >= today))
                .OrderByDescending(o => o.CreatedAt)
                .Take(200)
                .ToListAsync();
            var ids = orders.Select(o => o.Id).ToList();
            var receipts = await _context.FiscalReceipts.AsNoTracking().Where(r => r.OrderId != null && ids.Contains(r.OrderId.Value) && r.CheckId == null).OrderBy(r => r.Id).ToListAsync();
            var pending = await _context.OrderItemVoids.AsNoTracking().Where(v => ids.Contains(v.OrderId) && v.Status == VoidStatuses.Pending)
                .GroupBy(v => v.OrderId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
            var names = await WaiterController.StaffNamesAsync(_context, orders.Select(o => o.ClosedBy).Concat(orders.Select(o => o.StaffUserId)));

            return Ok(orders.Select(o =>
            {
                var mine = receipts.Where(r => r.OrderId == o.Id).ToList();
                var last = mine.LastOrDefault();
                return new
                {
                    o.Id,
                    o.CreatedAt,
                    o.CustomerName,
                    o.Phone,
                    o.DeliveryType,
                    o.Address,
                    o.Status,
                    o.PaymentMethod,
                    o.PaymentStatus,
                    o.Total,
                    o.DeliveryPrice,
                    o.ClosedAt,
                    closedBy = o.ClosedBy == null ? null : names.GetValueOrDefault(o.ClosedBy.Value),
                    staffName = o.StaffUserId == null ? null : names.GetValueOrDefault(o.StaffUserId.Value),
                    o.StaffReason,
                    pendingVoids = pending.GetValueOrDefault(o.Id),
                    canClose = o.ClosedAt == null && o.Status != OrderStatuses.Cancelled && Handed.Contains(o.Status),
                    fiscal = last == null ? null : new { last.Kind, last.Status, last.Error, last.FiscalDocumentNumber, sold = FiscalService.IsSold(mine) },
                };
            }));
        }

        // POST: api/site-orders/{id}/close — заказ выдан/доставлен: оплата, чек в АТОЛ или на сотрудника
        [HttpPost("{id:int}/close")]
        public async Task<IActionResult> Close(int id, [FromBody] CloseRequest r)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
            if (order == null || order.Source != Source || order.CheckId != null) return NotFound(new { message = "Заказ с сайта не найден" });
            if (order.Status == OrderStatuses.Cancelled) return BadRequest(new { message = "Заказ отменён" });
            if (order.ClosedAt != null) return BadRequest(new { message = "Заказ уже закрыт" });
            if (!Handed.Contains(order.Status)) return BadRequest(new { message = "Заказ ещё не готов — закрывают после выдачи или доставки" });
            if (await _context.OrderItemVoids.AnyAsync(v => v.OrderId == id && v.Status == VoidStatuses.Pending))
                return BadRequest(new { message = "Есть блюда, которые ждут решения кухни об отмене" });

            var method = r.PaymentMethod?.Trim().ToLowerInvariant();
            var fiscalOn = await _fiscal.EnabledAsync();
            string? staffName = null;
            var now = DateTime.UtcNow;

            if (order.PaymentMethod == "online")
            {
                if (order.PaymentStatus != "paid") return BadRequest(new { message = "Онлайн-оплата не прошла" });
                if (method is not ("online" or null or "")) return BadRequest(new { message = "Заказ оплачен онлайн — способ оплаты не меняется" });
            }
            else if (method == CheckPayments.Staff)
            {
                var fo = await _fiscalSettings.GetAsync();
                if (!fo.StaffChecks) return BadRequest(new { message = "Закрытие на заведение выключено — включите его в «Настройке интеграций» → «Онлайн-касса»" });
                r.StaffUserId ??= User.GetUserId();
                if (r.StaffUserId is not int staffId || !(await WaiterController.StaffListAsync(_context)).TryGetValue(staffId, out staffName))
                    return BadRequest(new { message = "Выберите сотрудника" });
                var reason = r.StaffReason?.Trim();
                if (string.IsNullOrEmpty(reason)) reason = WaiterController.StaffReasons[0];
                order.PaymentMethod = CheckPayments.Staff;
                order.StaffUserId = staffId;
                order.StaffReason = reason.Length > 60 ? reason[..60] : reason;
            }
            else if (method is CheckPayments.Cash or CheckPayments.Card)
            {
                order.PaymentMethod = method;
                order.PaymentStatus = "paid";
                order.PaidAt = now;
            }
            else return BadRequest(new { message = "Выберите способ оплаты" });

            // Скидка при закрытии (не для оплаченных онлайн — сумма уже списана)
            if (r.DiscountValue is > 0 && !string.IsNullOrEmpty(r.DiscountType))
            {
                if (order.PaymentMethod == "online") return BadRequest(new { message = "Заказ оплачен онлайн — скидку уже не дать" });
                if (!User.HasPermission(Permissions.Discounts)) return StatusCode(403, new { message = "Нет права добавлять скидки" });
                if (WaiterController.ValidateDiscount(new WaiterController.DiscountRequest { Type = r.DiscountType, Value = r.DiscountValue }) is { } derr)
                    return BadRequest(new { message = derr });
                var goods = order.Total - order.DeliveryPrice;
                var d = DiscountTypes.Of(r.DiscountType, r.DiscountValue, Math.Max(0, goods));
                order.ManualDiscount += d;
                order.ManualDiscountBy = User.GetUserId();
                order.DiscountAmount += d;
                order.Total -= d;
            }

            order.ClosedAt = now;
            order.ClosedBy = User.GetUserId();
            await _context.SaveChangesAsync();

            // Заказ закрыт — финальный статус, если курьер или кухня его ещё не поставили
            var final = order.DeliveryType == "Доставка" ? OrderStatuses.Delivered : OrderStatuses.Completed;
            if (!OrderStatuses.IsCompleted(order.Status)) await _statusService.ChangeStatusAsync(order, final);

            _logger.LogInformation("📦 Заказ с сайта #{OrderId} закрыт: {Total} ₽ ({Method}{Staff}), закрыл {User}", id, order.Total, order.PaymentMethod,
                staffName == null ? "" : $": {staffName}", User.GetUserId());

            if (order.PaymentMethod == CheckPayments.Staff)
                return Ok(new { id, fiscal = (object?)null, fiscalSkipped = "staff", staff = staffName });
            if (order.PaymentMethod == "online" && !OnlineViaAtol)
                return Ok(new { id, fiscal = (object?)null, fiscalSkipped = "yookassa" });
            if (!fiscalOn)
                return Ok(new { id, fiscal = (object?)null, fiscalSkipped = "disabled" });
            return Ok(new { id, fiscal = await SellAsync(id), fiscalSkipped = (string?)null });
        }

        // POST: api/site-orders/{id}/fiscal — пробить (повторить) чек по закрытому заказу
        [HttpPost("{id:int}/fiscal")]
        public async Task<IActionResult> Fiscal(int id)
        {
            var order = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
            if (order == null || order.Source != Source) return NotFound(new { message = "Заказ с сайта не найден" });
            if (order.ClosedAt == null) return BadRequest(new { message = "Сначала закройте заказ" });
            return Ok(await SellAsync(id));
        }

        private async Task<object> SellAsync(int id)
        {
            try { return FiscalController.View(await _fiscal.PrintAsync(id, FiscalKinds.Sell, User.GetUserId(), HttpContext.RequestAborted, OnlineViaAtol)); }
            catch (FiscalException ex) { return new { status = FiscalStatuses.Error, error = ex.Message }; }
        }

        // POST: api/site-orders/{id}/cancel — отменить весь заказ: каждое блюдо по правилам отмены
        // (не приняли — сразу; приняли или готовят — ждём кухню; готово или отдано — со списанием).
        // Чек уже пробит — сначала чек возврата; онлайн-оплата возвращается при отмене заказа
        [HttpPost("{id:int}/cancel")]
        public async Task<IActionResult> Cancel(int id, [FromBody] CancelRequest r)
        {
            var reason = r.Reason?.Trim() ?? "";
            if (reason.Length == 0) return BadRequest(new { message = "Укажите причину" });
            var order = await _context.Orders.Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == id);
            if (order == null || order.Source != Source || order.CheckId != null) return NotFound(new { message = "Заказ с сайта не найден" });
            if (order.Status == OrderStatuses.Cancelled) return BadRequest(new { message = "Заказ уже отменён" });

            object? returned = null;
            var receipts = await _context.FiscalReceipts.AsNoTracking().Where(x => x.OrderId == id && x.CheckId == null).OrderBy(x => x.Id).ToListAsync();
            if (receipts.LastOrDefault()?.Status == FiscalStatuses.Processing)
                return BadRequest(new { message = "Касса ещё не ответила по чеку этого заказа — сначала проверьте его" });
            if (FiscalService.IsSold(receipts))
            {
                FiscalReceipt ret;
                try { ret = await _fiscal.PrintAsync(id, FiscalKinds.Return, User.GetUserId(), HttpContext.RequestAborted, OnlineViaAtol); }
                catch (FiscalException ex) { return BadRequest(new { message = $"Чек возврата не пробит: {ex.Message}. Заказ не отменён" }); }
                if (ret.Status != FiscalStatuses.Done)
                    return BadRequest(new { message = $"Чек возврата не пробит: {ret.Error ?? "касса не ответила"}. Заказ не отменён", returned = FiscalController.View(ret) });
                returned = FiscalController.View(ret);
            }

            int done, pending;
            try { (done, pending) = await _voids.CancelAllAsync(order.OrderItems.Select(i => i.Id), reason, User.GetUserId()); }
            catch (VoidService.VoidException ex) { return BadRequest(new { message = ex.Message }); }
            var status = await _context.Orders.Where(o => o.Id == id).Select(o => o.Status).FirstAsync();
            _logger.LogInformation("🗑 Заказ с сайта #{OrderId} отменяется ({Reason}): убрано {Done}, ждёт кухню {Pending}", id, reason, done, pending);
            return Ok(new { cancelled = status == OrderStatuses.Cancelled, done, pending, returned });
        }
    }
}
