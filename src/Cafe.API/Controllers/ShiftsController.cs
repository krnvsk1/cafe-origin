using Microsoft.AspNetCore.Authorization;
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
    /// <summary>Смены заведения и официантов; история смен с чеками — для менеджера</summary>
    [ApiController]
    [Route("api/shifts")]
    [Authorize]
    public class ShiftsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ShiftService _shifts;
        private readonly FiscalService _fiscal;
        private readonly ILogger<ShiftsController> _logger;
        private readonly WebPushService _push;

        public ShiftsController(ApplicationDbContext context, ShiftService shifts, FiscalService fiscal, ILogger<ShiftsController> logger, WebPushService push)
        {
            _push = push;
            _fiscal = fiscal;
            _logger = logger;
            _context = context;
            _shifts = shifts;
        }

        private IActionResult Fail(ShiftService.ShiftException ex) => BadRequest(new { message = ex.Message });

        // GET: api/shifts/current — открыта ли смена заведения, моя смена, кто сейчас работает
        [HttpGet("current")]
        public async Task<IActionResult> Current()
        {
            if (!User.IsStaff()) return Forbid();
            var me = User.GetUserId() ?? 0;
            var canVenue = await _shifts.HasAsync(User, Permissions.Shift);
            var seesAll = await _shifts.HasAsync(User, Permissions.ChecksAll);
            var venue = await _shifts.CurrentVenueAsync();
            var mine = await _shifts.CurrentWaiterAsync(me);
            var minePending = mine == null ? await _shifts.PendingWaiterAsync(me) : null;
            var canApprove = await _shifts.HasAsync(User, Permissions.ShiftApprove);
            var all = venue == null ? new List<WaiterShift>()
                : await _context.WaiterShifts.AsNoTracking().Where(w => w.VenueShiftId == venue.Id && w.ClosedAt == null).OrderBy(w => w.OpenedAt).ToListAsync();
            var waiters = all.Where(w => w.ApprovedAt != null).ToList();
            var pendingShifts = all.Where(w => w.ApprovedAt == null).ToList();
            var names = await WaiterController.StaffNamesAsync(_context, all.Select(w => (int?)w.UserId).Concat(all.Select(w => w.ApprovedBy))
                .Append(mine?.ApprovedBy).Append(venue?.OpenedByUserId));
            // Кто подтвердил смену (если официант открыл её сам — не показываем)
            string? ApprovedByName(WaiterShift w) => w.ApprovedBy == null || w.ApprovedBy == w.UserId ? null : names.GetValueOrDefault(w.ApprovedBy.Value);
            var openChecks = await _context.TableChecks.AsNoTracking().Where(c => c.Status == CheckStatuses.Open)
                .GroupBy(c => c.WaiterId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key ?? 0, x => x.Count);
            // Непробитые кассовые чеки — менеджер видит их до закрытия смены
            int? unfiscalized = (canVenue || seesAll) && await _fiscal.EnabledAsync()
                ? (await _fiscal.UnfinishedAsync(DateTime.UtcNow.AddDays(-UnfinishedDays))).Count
                : null;

            return Ok(new
            {
                canManageVenue = canVenue,
                seesAll,
                venue = venue == null ? null : new
                {
                    venue.Id,
                    venue.BusinessDate,
                    venue.OpenedAt,
                    openedBy = names.GetValueOrDefault(venue.OpenedByUserId ?? 0),
                    // Смена с прошлого дня не закрыта
                    stale = venue.BusinessDate < ShiftService.BusinessDate(DateTime.UtcNow),
                },
                mine = mine == null || venue == null || mine.VenueShiftId != venue.Id ? null : new { mine.Id, mine.OpenedAt, openChecks = openChecks.GetValueOrDefault(me), approvedBy = ApprovedByName(mine) },
                // Мой запрос на смену ждёт подтверждения менеджера
                minePending = minePending == null || venue == null || minePending.VenueShiftId != venue.Id ? null : new { minePending.Id, requestedAt = minePending.OpenedAt },
                canApprove,
                // Запросы официантов на открытие смены — тем, кто может подтверждать
                pendingWaiters = canApprove
                    ? pendingShifts.Select(w => new { w.Id, w.UserId, name = names.GetValueOrDefault(w.UserId), requestedAt = w.OpenedAt })
                    : null,
                waiters = canVenue || seesAll
                    ? waiters.Select(w => new { w.Id, w.UserId, name = names.GetValueOrDefault(w.UserId), w.OpenedAt, openChecks = openChecks.GetValueOrDefault(w.UserId), approvedBy = ApprovedByName(w) })
                    : null,
                totalOpenChecks = canVenue || seesAll ? openChecks.Values.Sum() : (int?)null,
                unfiscalized,
            });
        }

        private const int UnfinishedDays = 14;

        // GET: api/shifts/fiscal/unfinished — кассовые чеки, которые не пробились (или касса не ответила) за 2 недели
        [HttpGet("fiscal/unfinished")]
        [HasPermission(Permissions.ChecksAll, Permissions.Pos)]
        public async Task<IActionResult> Unfinished()
        {
            if (!await _fiscal.EnabledAsync()) return Ok(Array.Empty<object>());
            var list = await _fiscal.UnfinishedAsync(DateTime.UtcNow.AddDays(-UnfinishedDays));
            var checkIds = list.Where(r => r.CheckId != null).Select(r => r.CheckId!.Value).Distinct().ToList();
            var tables = await _context.TableChecks.AsNoTracking().Include(c => c.Table).Where(c => checkIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => new { table = c.Table != null ? c.Table.Name : null, c.Label, c.ClosedAt });
            return Ok(list.Select(r => new
            {
                r.Id,
                r.OrderId,
                r.CheckId,
                r.BanquetId,
                r.BanquetPaymentId,
                r.Kind,
                r.Status,
                r.Error,
                r.Total,
                r.PaymentMethod,
                r.CreatedAt,
                table = r.CheckId != null ? tables.GetValueOrDefault(r.CheckId.Value)?.table : null,
                label = r.CheckId != null ? tables.GetValueOrDefault(r.CheckId.Value)?.Label : null,
            }));
        }

        // POST: api/shifts/venue/open — открыть смену заведения на день
        [HttpPost("venue/open")]
        [HasPermission(Permissions.Shift)]
        public async Task<IActionResult> OpenVenue()
        {
            try
            {
                var s = await _shifts.OpenVenueAsync(User.GetUserId());
                return Ok(new { s.Id, fiscal = await SyncKktShiftAsync(open: true) });
            }
            catch (ShiftService.ShiftException ex) { return Fail(ex); }
        }

        /// <summary>
        /// Смена на онлайн-кассе АТОЛ вслед за сменой заведения: открыли смену — открываем на кассе
        /// (просроченную больше 24 часов сначала закрываем), закрыли — закрываем на кассе (Z-отчёт).
        /// Ошибка кассы смену заведения не отменяет — возвращается текстом для сотрудника.
        /// </summary>
        private async Task<object?> SyncKktShiftAsync(bool open)
        {
            if (!await _fiscal.EnabledAsync()) return null;
            var ct = HttpContext.RequestAborted;
            try
            {
                var o = await HttpContext.RequestServices.GetRequiredService<FiscalSettingsProvider>().GetAsync();
                var (state, _) = await _fiscal.ShiftStatusAsync(o, ct);
                if (open)
                {
                    string message;
                    if (state == "opened") message = "Смена на кассе уже открыта";
                    else
                    {
                        if (state == "expired") await _fiscal.ShiftAsync("closeShift", User.GetUserId(), ct);
                        await _fiscal.ShiftAsync("openShift", User.GetUserId(), ct);
                        message = state == "expired" ? "Старая смена на кассе закрыта, новая открыта" : "Смена на кассе открыта";
                    }
                    // Отчёт о состоянии счётчиков без гашения (X-отчёт): наличные в кассе на начало смены.
                    // Не напечатался — смена всё равно открыта, отчёт можно снять на кассе (меню «Касса» → X-отчёт)
                    try
                    {
                        await _fiscal.ShiftAsync("reportX", User.GetUserId(), ct);
                        message += ", X-отчёт напечатан";
                    }
                    catch (FiscalException ex)
                    {
                        return new { ok = false, message = $"{message}, но X-отчёт не напечатан: {ex.Message}" };
                    }
                    return new { ok = true, message };
                }
                if (state == "closed") return new { ok = true, message = "Смена на кассе уже закрыта" };
                await _fiscal.ShiftAsync("closeShift", User.GetUserId(), ct);
                return new { ok = true, message = "Смена на кассе закрыта, Z-отчёт напечатан" };
            }
            catch (FiscalException ex)
            {
                _logger.LogWarning("Смена на кассе АТОЛ не {Op}: {Error}", open ? "открыта" : "закрыта", ex.Message);
                return new { ok = false, message = $"Касса АТОЛ: {ex.Message}. Откройте/закройте смену на кассе вручную в «Кассе»." };
            }
        }

        // POST: api/shifts/venue/close — закрыть смену заведения (открытых счетов быть не должно)
        public record BanquetRow(int Id, string CustomerName, DateTime EventAt, decimal Total, DateTime? ClosedAt, string? ClosedBy,
            decimal Cash, decimal Card, decimal Staff, decimal Prepaid);

        public class BanquetTotals
        {
            public static readonly BanquetTotals Empty = new();
            public decimal Cash, Card, Staff;
            public int Closed;
            public List<BanquetRow> List = new();
        }

        /// <summary>
        /// Банкеты по сменам: деньги — по времени оплаты (как в кассе), список — банкеты, закрытые за время смены
        /// </summary>
        private async Task<Dictionary<int, BanquetTotals>> BanquetsInAsync(List<VenueShift> shifts)
        {
            var result = new Dictionary<int, BanquetTotals>();
            if (shifts.Count == 0) return result;
            var now = DateTime.UtcNow;
            var from = shifts.Min(s => s.OpenedAt);
            var to = shifts.Max(s => s.ClosedAt ?? now);
            var pays = await _context.BanquetPayments.AsNoTracking()
                .Where(p => p.RefundedAt == null && p.CreatedAt >= from && p.CreatedAt <= to).ToListAsync();
            var closed = await _context.Banquets.AsNoTracking()
                .Where(b => b.Status == BanquetStatuses.Closed && b.ClosedAt >= from && b.ClosedAt <= to).ToListAsync();
            var closedIds = closed.Select(b => b.Id).ToList();
            var closedPays = await _context.BanquetPayments.AsNoTracking()
                .Where(p => closedIds.Contains(p.BanquetId) && p.RefundedAt == null).ToListAsync();
            var names = await WaiterController.StaffNamesAsync(_context, closed.Select(b => b.ClosedBy));
            foreach (var s in shifts)
            {
                var end = s.ClosedAt ?? now;
                var t = new BanquetTotals();
                foreach (var p in pays.Where(p => p.CreatedAt >= s.OpenedAt && p.CreatedAt <= end))
                {
                    if (p.Method == CheckPayments.Cash) t.Cash += p.Amount;
                    else if (p.Method == CheckPayments.Card) t.Card += p.Amount;
                    else if (p.Method == CheckPayments.Staff) t.Staff += p.Amount;
                }
                foreach (var b in closed.Where(b => b.ClosedAt >= s.OpenedAt && b.ClosedAt <= end).OrderBy(b => b.ClosedAt))
                {
                    var mine = closedPays.Where(p => p.BanquetId == b.Id).ToList();
                    t.List.Add(new BanquetRow(b.Id, b.CustomerName, b.EventAt, b.Total, b.ClosedAt, b.ClosedBy == null ? null : names.GetValueOrDefault(b.ClosedBy.Value),
                        mine.Where(p => p.Kind != BanquetPaymentKinds.Prepay && p.Method == CheckPayments.Cash).Sum(p => p.Amount),
                        mine.Where(p => p.Kind != BanquetPaymentKinds.Prepay && p.Method == CheckPayments.Card).Sum(p => p.Amount),
                        mine.Where(p => p.Method == CheckPayments.Staff).Sum(p => p.Amount),
                        mine.Where(p => p.Kind == BanquetPaymentKinds.Prepay && p.Method != CheckPayments.Staff).Sum(p => p.Amount)));
                }
                t.Closed = t.List.Count;
                if (t.Cash != 0 || t.Card != 0 || t.Staff != 0 || t.Closed > 0) result[s.Id] = t;
            }
            return result;
        }

        // GET: api/shifts/venue/report — итоги текущей смены перед закрытием: нал, безнал, Наличные (Принтер), возвраты
        [HttpGet("venue/report")]
        [HasPermission(Permissions.Shift)]
        public async Task<IActionResult> VenueReport()
        {
            var s = await _context.VenueShifts.AsNoTracking().Where(x => x.ClosedAt == null).OrderByDescending(x => x.OpenedAt).FirstOrDefaultAsync();
            if (s == null) return NotFound(new { message = "Смена заведения не открыта" });
            var from = s.OpenedAt;
            var refunds = await Refunds.LoadAsync(_context);

            // Зал: счета этой смены
            var checks = await _context.TableChecks.AsNoTracking().Where(c => c.VenueShiftId == s.Id).ToListAsync();
            var closed = checks.Where(c => c.Status == CheckStatuses.Closed).ToList();
            var live = closed.Where(c => !refunds.Checks.Contains(c.Id)).ToList();
            var open = checks.Where(c => c.Status == CheckStatuses.Open).Select(c => c.Id).ToList();
            var openTotals = await OpenTotalsAsync(open);
            var hall = new
            {
                checks = closed.Count,
                cash = live.Where(c => c.PaymentMethod == CheckPayments.Cash).Sum(c => c.Total),
                card = live.Where(c => c.PaymentMethod == CheckPayments.Card).Sum(c => c.Total),
                staff = live.Where(c => c.PaymentMethod == CheckPayments.Staff).Sum(c => c.Total),
                refunds = closed.Where(c => refunds.Checks.Contains(c.Id)).Sum(c => c.Total),
                open = open.Count,
                openTotal = openTotals.Values.Sum(),
            };

            // Заказы с сайта, закрытые за смену
            var siteOrders = await _context.Orders.AsNoTracking()
                .Where(o => o.Source == SiteOrdersController.Source && o.ClosedAt != null && o.ClosedAt >= from && o.Status != OrderStatuses.Cancelled)
                .Select(o => new { o.Id, o.PaymentMethod, o.Total }).ToListAsync();
            var siteLive = siteOrders.Where(o => !refunds.Orders.Contains(o.Id)).ToList();
            var site = new
            {
                orders = siteOrders.Count,
                cash = siteLive.Where(o => o.PaymentMethod == CheckPayments.Cash).Sum(o => o.Total),
                card = siteLive.Where(o => o.PaymentMethod == CheckPayments.Card).Sum(o => o.Total),
                online = siteLive.Where(o => o.PaymentMethod == "online").Sum(o => o.Total),
                staff = siteLive.Where(o => o.PaymentMethod == CheckPayments.Staff).Sum(o => o.Total),
                refunds = siteOrders.Where(o => refunds.Orders.Contains(o.Id)).Sum(o => o.Total),
            };

            // Банкеты: оплаты, внесённые за смену
            var pays = await _context.BanquetPayments.AsNoTracking().Where(p => p.CreatedAt >= from && p.RefundedAt == null)
                .Select(p => new { p.Method, p.Amount }).ToListAsync();
            var banquets = new
            {
                payments = pays.Count,
                cash = pays.Where(p => p.Method == CheckPayments.Cash).Sum(p => p.Amount),
                card = pays.Where(p => p.Method == CheckPayments.Card).Sum(p => p.Amount),
                staff = pays.Where(p => p.Method == CheckPayments.Staff).Sum(p => p.Amount),
            };

            var cash = hall.cash + site.cash + banquets.cash;
            var card = hall.card + site.card + banquets.card;
            return Ok(new
            {
                s.Id,
                s.BusinessDate,
                s.OpenedAt,
                hall,
                site,
                banquets,
                totals = new
                {
                    cash,
                    card,
                    online = site.online,
                    staff = hall.staff + site.staff + banquets.staff,
                    refunds = hall.refunds + site.refunds,
                    revenue = cash + card + site.online + hall.staff + site.staff + banquets.staff,
                },
            });
        }

        [HttpPost("venue/close")]
        [HasPermission(Permissions.Shift)]
        public async Task<IActionResult> CloseVenue()
        {
            try
            {
                var s = await _shifts.CloseVenueAsync(User.GetUserId());
                return Ok(new { s.Id, fiscal = await SyncKktShiftAsync(open: false) });
            }
            catch (ShiftService.ShiftException ex) { return Fail(ex); }
        }

        // POST: api/shifts/mine/open — официант начинает смену
        [HttpPost("mine/open")]
        [HasPermission(Permissions.Waiter)]
        public async Task<IActionResult> OpenMine()
        {
            var me = User.GetUserId();
            if (me == null) return Unauthorized();
            // Без права «Смена без подтверждения» (или «Подтверждение смен») — смену подтверждает менеджер или администратор
            var approved = await _shifts.HasAsync(User, Permissions.ShiftSelf) || await _shifts.HasAsync(User, Permissions.ShiftApprove);
            try
            {
                var w = await _shifts.OpenWaiterAsync(me.Value, approved);
                if (w.ApprovedAt == null)
                {
                    _logger.LogInformation("⏳ Официант #{UserId} просит открыть смену", me);
                    // Оповещение всем, кто может подтвердить
                    var approvers = await ApproversAsync();
                    var name = (await WaiterController.StaffNamesAsync(_context, new int?[] { me })).GetValueOrDefault(me.Value) ?? "Официант";
                    _push.Enqueue(approvers.Where(id => id != me.Value).ToList(), new PushMessage(
                        "Подтвердите смену официанта", $"{name} просит открыть смену", "/admin/shifts", $"shift-request-{w.Id}"));
                }
                return Ok(new { w.Id, pending = w.ApprovedAt == null });
            }
            catch (ShiftService.ShiftException ex) { return Fail(ex); }
        }

        // POST: api/shifts/waiters/{id}/approve — подтвердить смену официанта
        [HttpPost("waiters/{id:int}/approve")]
        [HasPermission(Permissions.ShiftApprove)]
        public async Task<IActionResult> ApproveWaiter(int id)
        {
            try
            {
                var w = await _shifts.ApproveWaiterAsync(id, User.GetUserId());
                _logger.LogInformation("✅ Смена официанта #{UserId} подтверждена ({By})", w.UserId, User.GetUserId());
                var by = (await WaiterController.StaffNamesAsync(_context, new[] { User.GetUserId() })).GetValueOrDefault(User.GetUserId() ?? 0);
                _push.Enqueue(new[] { w.UserId }, new PushMessage("Смена подтверждена", by != null ? $"Подтвердил(а) {by} — можно работать" : "Можно работать", "/waiter", $"shift-ok-{w.Id}"));
                return Ok(new { w.Id });
            }
            catch (ShiftService.ShiftException ex) { return Fail(ex); }
        }

        // POST: api/shifts/waiters/{id}/reject — отклонить запрос на смену
        [HttpPost("waiters/{id:int}/reject")]
        [HasPermission(Permissions.ShiftApprove)]
        public async Task<IActionResult> RejectWaiter(int id)
        {
            try
            {
                var w = await _shifts.RejectWaiterAsync(id);
                _logger.LogInformation("⛔ Запрос официанта #{UserId} на смену отклонён ({By})", w.UserId, User.GetUserId());
                var by = (await WaiterController.StaffNamesAsync(_context, new[] { User.GetUserId() })).GetValueOrDefault(User.GetUserId() ?? 0);
                _push.Enqueue(new[] { w.UserId }, new PushMessage("Смена не подтверждена", by != null ? $"Отклонил(а) {by}" : "Запрос отклонён", "/waiter", $"shift-no-{w.Id}"));
                return Ok(new { w.Id });
            }
            catch (ShiftService.ShiftException ex) { return Fail(ex); }
        }

        // POST: api/shifts/mine/close — официант заканчивает смену (свои счета должны быть закрыты)
        [HttpPost("mine/close")]
        [HasPermission(Permissions.Waiter)]
        public async Task<IActionResult> CloseMine()
        {
            var me = User.GetUserId();
            if (me == null) return Unauthorized();
            try { var w = await _shifts.CloseWaiterAsync(me.Value); return Ok(new { w.Id }); }
            catch (ShiftService.ShiftException ex) { return Fail(ex); }
        }

        /// <summary>Кто может подтверждать смены: администраторы и роли с правом «Подтверждение смен официантов»</summary>
        private async Task<List<int>> ApproversAsync()
        {
            var roleIds = await _context.Roles
                .Where(r => r.Name == AppRoles.Support || _context.RoleClaims.Any(c => c.RoleId == r.Id && c.ClaimType == Permissions.ClaimType && c.ClaimValue == Permissions.ShiftApprove))
                .Select(r => r.Id).ToListAsync();
            return await _context.UserRoles.Where(ur => roleIds.Contains(ur.RoleId)).Select(ur => ur.UserId).Distinct().ToListAsync();
        }

        /// <summary>Сумма открытого счёта — по его блюдам; закрытого — как при оплате</summary>
        private async Task<Dictionary<int, decimal>> OpenTotalsAsync(List<int> openIds)
        {
            if (openIds.Count == 0) return new();
            var orders = await _context.Orders.AsNoTracking().Include(o => o.OrderItems).ThenInclude(i => i.SelectedAddons)
                .Where(o => o.Status != OrderStatuses.Cancelled &&
                            ((o.CheckId != null && openIds.Contains(o.CheckId.Value)) || o.OrderItems.Any(i => i.CheckId != null && openIds.Contains(i.CheckId.Value))))
                .ToListAsync();
            return orders.SelectMany(o => o.OrderItems.Select(i => (Check: i.CheckId ?? o.CheckId ?? 0, Sum: WaiterController.LineTotal(i))))
                .Where(x => openIds.Contains(x.Check)).GroupBy(x => x.Check).ToDictionary(g => g.Key, g => g.Sum(x => x.Sum));
        }

        // GET: api/shifts?from&to — смены заведения за период (по Москве), в том числе перенесённые из Interpos
        [HttpGet]
        [HasPermission(Permissions.ChecksAll)]
        public async Task<IActionResult> List([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var today = ShiftService.BusinessDate(DateTime.UtcNow);
            var f = (from ?? today.AddDays(-13)).Date;
            var t = (to ?? today).Date;
            if (t < f) (f, t) = (t, f);

            var shifts = await _context.VenueShifts.AsNoTracking().Where(s => s.BusinessDate >= f && s.BusinessDate <= t)
                .OrderByDescending(s => s.OpenedAt).ToListAsync();
            var ids = shifts.Select(s => s.Id).ToList();
            // Итоги по чекам считает база; возвращённые счета (деньги вернули гостю) — , отдельно «Возвраты»
            var refunds = await Refunds.LoadAsync(_context);
            var refunded = refunds.Checks.ToList();
            var totals = await _context.TableChecks.AsNoTracking()
                .Where(c => c.VenueShiftId != null && ids.Contains(c.VenueShiftId.Value))
                .GroupBy(c => c.VenueShiftId!.Value)
                .Select(g => new
                {
                    ShiftId = g.Key,
                    Checks = g.Count(),
                    Open = g.Count(c => c.Status == CheckStatuses.Open),
                    Revenue = g.Sum(c => c.Status == CheckStatuses.Closed && c.PaymentMethod != CheckPayments.Staff && !refunded.Contains(c.Id) ? c.Total : 0m),
                    Cash = g.Sum(c => c.Status == CheckStatuses.Closed && c.PaymentMethod == CheckPayments.Cash && !refunded.Contains(c.Id) ? c.Total : 0m),
                    Card = g.Sum(c => c.Status == CheckStatuses.Closed && c.PaymentMethod == CheckPayments.Card && !refunded.Contains(c.Id) ? c.Total : 0m),
                    Staff = g.Sum(c => c.Status == CheckStatuses.Closed && c.PaymentMethod == CheckPayments.Staff && c.RefundedAt == null ? c.Total : 0m),
                    Refunds = g.Sum(c => c.Status == CheckStatuses.Closed && refunded.Contains(c.Id) ? c.Total : 0m),
                })
                .ToDictionaryAsync(x => x.ShiftId);
            // Банкеты: оплаты (предоплата и расчёт), внесённые за время смены, и банкеты, закрытые в смену
            var bq = await BanquetsInAsync(shifts);
            var waiters = await _context.WaiterShifts.AsNoTracking().Where(w => ids.Contains(w.VenueShiftId) && w.ApprovedAt != null)
                .GroupBy(w => w.VenueShiftId).Select(g => new { ShiftId = g.Key, Count = g.Select(w => w.UserId).Distinct().Count() })
                .ToDictionaryAsync(x => x.ShiftId, x => x.Count);
            var names = await WaiterController.StaffNamesAsync(_context, shifts.SelectMany(s => new[] { s.OpenedByUserId, s.ClosedByUserId }));

            return Ok(shifts.Select(s =>
            {
                var x = totals.GetValueOrDefault(s.Id);
                var b = bq.GetValueOrDefault(s.Id) ?? BanquetTotals.Empty;
                return new
                {
                    s.Id,
                    s.BusinessDate,
                    s.OpenedAt,
                    s.ClosedAt,
                    openedBy = names.GetValueOrDefault(s.OpenedByUserId ?? 0),
                    closedBy = names.GetValueOrDefault(s.ClosedByUserId ?? 0),
                    waiters = waiters.GetValueOrDefault(s.Id),
                    checks = x?.Checks ?? 0,
                    openChecks = x?.Open ?? 0,
                    revenue = (x?.Revenue ?? 0) + b.Cash + b.Card,
                    cash = (x?.Cash ?? 0) + b.Cash,
                    card = (x?.Card ?? 0) + b.Card,
                    staff = (x?.Staff ?? 0) + b.Staff,
                    refunds = x?.Refunds ?? 0,
                    banquets = b.Closed,
                    banquetRevenue = b.Cash + b.Card,
                };
            }));
        }

        // GET: api/shifts/{id} — смена: официанты и все чеки
        [HttpGet("{id:int}")]
        [HasPermission(Permissions.ChecksAll)]
        public async Task<IActionResult> Get(int id)
        {
            var s = await _context.VenueShifts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (s == null) return NotFound(new { message = "Смена не найдена" });
            var checks = await _context.TableChecks.AsNoTracking().Include(c => c.Table).Where(c => c.VenueShiftId == id).OrderBy(c => c.OpenedAt).ToListAsync();
            var waiters = await _context.WaiterShifts.AsNoTracking().Where(w => w.VenueShiftId == id && w.ApprovedAt != null).OrderBy(w => w.OpenedAt).ToListAsync();
            var names = await WaiterController.StaffNamesAsync(_context,
                waiters.Select(w => (int?)w.UserId).Concat(waiters.Select(w => w.ApprovedBy)).Concat(checks.Select(c => c.WaiterId)).Concat(checks.Select(c => c.StaffUserId)).Concat(checks.Select(c => c.PaymentChangedBy)).Concat(checks.Select(c => c.RefundedBy))
                    .Append(s.OpenedByUserId).Append(s.ClosedByUserId));
            var staffChecks = checks.Where(c => c.Status == CheckStatuses.Closed && c.PaymentMethod == CheckPayments.Staff && c.RefundedAt == null).ToList();
            var openTotals = await OpenTotalsAsync(checks.Where(c => c.Status == CheckStatuses.Open).Select(c => c.Id).ToList());
            var ids = checks.Select(c => (int?)c.Id).ToList();
            var receipts = await _context.FiscalReceipts.AsNoTracking().Where(r => ids.Contains(r.CheckId)).OrderBy(r => r.Id).ToListAsync();
            // Номера заказов кухни по счёту — «Стол 5 · Заказ #8328»
            var checkOrders = (await _context.OrderItems.AsNoTracking()
                    .Where(i => ids.Contains(i.CheckId ?? i.Order!.CheckId))
                    .Select(i => new { CheckId = i.CheckId ?? i.Order!.CheckId, i.OrderId }).Distinct().ToListAsync())
                .GroupBy(x => x.CheckId!.Value).ToDictionary(g => g.Key, g => g.Select(x => x.OrderId).Distinct().OrderBy(x => x).ToList());
            // Продажа и возврат могут чередоваться (сменили оплату): показываем то, что после последнего пробитого чека другого вида
            object? FiscalOf(int checkId, string kind)
            {
                var all = receipts.Where(r => r.CheckId == checkId).ToList();
                var other = kind == FiscalKinds.Sell ? FiscalKinds.Return : FiscalKinds.Sell;
                var lastOther = all.LastOrDefault(x => x.Kind == other && x.Status == FiscalStatuses.Done)?.Id ?? 0;
                var mine = all.Where(r => r.Kind == kind && r.Id > lastOther).ToList();
                var r = mine.LastOrDefault(x => x.Status == FiscalStatuses.Done) ?? mine.LastOrDefault();
                return r == null ? null : new { r.Status, r.Error, r.FiscalDocumentNumber };
            }
            // Возвращён: пробитых возвратов не меньше, чем продаж — деньги отдали гостю, это 
            bool Refunded(int checkId)
            {
                if (checks.Any(c => c.Id == checkId && c.RefundedAt != null)) return true;
                var done = receipts.Where(r => r.CheckId == checkId && r.Status == FiscalStatuses.Done).ToList();
                var ret = done.Count(r => r.Kind == FiscalKinds.Return);
                return ret > 0 && ret >= done.Count(r => r.Kind == FiscalKinds.Sell);
            }
            var bq = (await BanquetsInAsync(new List<VenueShift> { s })).GetValueOrDefault(s.Id) ?? BanquetTotals.Empty;
            decimal Total(TableCheck c) => c.Status == CheckStatuses.Open ? WaiterController.Net(c, openTotals.GetValueOrDefault(c.Id)) : c.Status == CheckStatuses.Closed ? c.Total : 0;

            return Ok(new
            {
                s.Id,
                s.BusinessDate,
                s.OpenedAt,
                s.ClosedAt,
                openedBy = names.GetValueOrDefault(s.OpenedByUserId ?? 0),
                closedBy = names.GetValueOrDefault(s.ClosedByUserId ?? 0),
                waiters = waiters.Select(w =>
                {
                    var mine = checks.Where(c => c.WaiterShiftId == w.Id).ToList();
                    return new
                    {
                        w.Id,
                        w.UserId,
                        name = names.GetValueOrDefault(w.UserId),
                        w.OpenedAt,
                        w.ClosedAt,
                        approvedBy = w.ApprovedBy == null || w.ApprovedBy == w.UserId ? null : names.GetValueOrDefault(w.ApprovedBy.Value),
                        checks = mine.Count,
                        revenue = mine.Where(c => c.Status == CheckStatuses.Closed && c.PaymentMethod != CheckPayments.Staff && !Refunded(c.Id)).Sum(c => c.Total),
                    };
                }),
                // На сотрудников: без оплаты и без чека — суммы к удержанию по людям
                staff = staffChecks.GroupBy(c => c.StaffUserId ?? 0).Select(g => new
                {
                    userId = g.Key,
                    name = names.GetValueOrDefault(g.Key) ?? "—",
                    checks = g.Count(),
                    total = g.Sum(c => c.Total),
                }).OrderByDescending(x => x.total),
                // Банкеты, закрытые в эту смену, и деньги по банкетам, внесённые за смену
                banquets = bq.List,
                banquetMoney = new { cash = bq.Cash, card = bq.Card, staff = bq.Staff },
                checks = checks.Select(c => new
                {
                    c.Id,
                    table = c.Table?.Name,
                    c.Label,
                    orderIds = checkOrders.GetValueOrDefault(c.Id) ?? new List<int>(),
                    waiter = names.GetValueOrDefault(c.WaiterId ?? 0),
                    c.WaiterId,
                    c.Status,
                    c.Guests,
                    c.OpenedAt,
                    c.ClosedAt,
                    c.PaymentMethod,
                    staffName = c.StaffUserId == null ? null : names.GetValueOrDefault(c.StaffUserId.Value),
                    c.StaffReason,
                    c.PaymentWas,
                    paymentChangedBy = c.PaymentChangedBy == null ? null : names.GetValueOrDefault(c.PaymentChangedBy.Value),
                    c.PaymentChangedAt,
                    total = Total(c),
                    fiscal = FiscalOf(c.Id, FiscalKinds.Sell),
                    fiscalReturn = FiscalOf(c.Id, FiscalKinds.Return),
                    refunded = c.Status == CheckStatuses.Closed && Refunded(c.Id),
                    refundedBy = c.RefundedBy == null ? null : names.GetValueOrDefault(c.RefundedBy.Value),
                    c.RefundedAt,
                })
            });
        }
    }
}
