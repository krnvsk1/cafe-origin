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

        public ShiftsController(ApplicationDbContext context, ShiftService shifts, FiscalService fiscal, ILogger<ShiftsController> logger)
        {
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
            var waiters = venue == null ? new List<WaiterShift>()
                : await _context.WaiterShifts.AsNoTracking().Where(w => w.VenueShiftId == venue.Id && w.ClosedAt == null).OrderBy(w => w.OpenedAt).ToListAsync();
            var names = await WaiterController.StaffNamesAsync(_context, waiters.Select(w => (int?)w.UserId).Append(venue?.OpenedByUserId));
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
                mine = mine == null || venue == null || mine.VenueShiftId != venue.Id ? null : new { mine.Id, mine.OpenedAt, openChecks = openChecks.GetValueOrDefault(me) },
                waiters = canVenue || seesAll
                    ? waiters.Select(w => new { w.Id, w.UserId, name = names.GetValueOrDefault(w.UserId), w.OpenedAt, openChecks = openChecks.GetValueOrDefault(w.UserId) })
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
                    if (state == "opened") return new { ok = true, message = "Смена на кассе уже открыта" };
                    if (state == "expired") await _fiscal.ShiftAsync("closeShift", User.GetUserId(), ct);
                    await _fiscal.ShiftAsync("openShift", User.GetUserId(), ct);
                    return new { ok = true, message = state == "expired" ? "Старая смена на кассе закрыта, новая открыта" : "Смена на кассе открыта" };
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
            try { var w = await _shifts.OpenWaiterAsync(me.Value); return Ok(new { w.Id }); }
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

        // GET: api/shifts?from&to — смены заведения за период (по Москве, до 92 дней)
        [HttpGet]
        [HasPermission(Permissions.ChecksAll)]
        public async Task<IActionResult> List([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var today = ShiftService.BusinessDate(DateTime.UtcNow);
            var f = (from ?? today.AddDays(-13)).Date;
            var t = (to ?? today).Date;
            if (t < f) (f, t) = (t, f);
            if ((t - f).TotalDays > 92) return BadRequest(new { message = "Период — не больше 3 месяцев" });

            var shifts = await _context.VenueShifts.AsNoTracking().Where(s => s.BusinessDate >= f && s.BusinessDate <= t)
                .OrderByDescending(s => s.OpenedAt).ToListAsync();
            var ids = shifts.Select(s => s.Id).ToList();
            var checks = await _context.TableChecks.AsNoTracking().Where(c => c.VenueShiftId != null && ids.Contains(c.VenueShiftId.Value)).ToListAsync();
            var waiters = await _context.WaiterShifts.AsNoTracking().Where(w => ids.Contains(w.VenueShiftId)).ToListAsync();
            var names = await WaiterController.StaffNamesAsync(_context, shifts.SelectMany(s => new[] { s.OpenedByUserId, s.ClosedByUserId }));

            return Ok(shifts.Select(s =>
            {
                var cs = checks.Where(c => c.VenueShiftId == s.Id).ToList();
                var closed = cs.Where(c => c.Status == CheckStatuses.Closed).ToList();
                return new
                {
                    s.Id,
                    s.BusinessDate,
                    s.OpenedAt,
                    s.ClosedAt,
                    openedBy = names.GetValueOrDefault(s.OpenedByUserId ?? 0),
                    closedBy = names.GetValueOrDefault(s.ClosedByUserId ?? 0),
                    waiters = waiters.Where(w => w.VenueShiftId == s.Id).Select(w => w.UserId).Distinct().Count(),
                    checks = cs.Count,
                    openChecks = cs.Count(c => c.Status == CheckStatuses.Open),
                    revenue = closed.Sum(c => c.Total),
                    cash = closed.Where(c => c.PaymentMethod == "cash").Sum(c => c.Total),
                    card = closed.Where(c => c.PaymentMethod == "card").Sum(c => c.Total),
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
            var waiters = await _context.WaiterShifts.AsNoTracking().Where(w => w.VenueShiftId == id).OrderBy(w => w.OpenedAt).ToListAsync();
            var names = await WaiterController.StaffNamesAsync(_context,
                waiters.Select(w => (int?)w.UserId).Concat(checks.Select(c => c.WaiterId)).Append(s.OpenedByUserId).Append(s.ClosedByUserId));
            var openTotals = await OpenTotalsAsync(checks.Where(c => c.Status == CheckStatuses.Open).Select(c => c.Id).ToList());
            var ids = checks.Select(c => (int?)c.Id).ToList();
            var receipts = await _context.FiscalReceipts.AsNoTracking().Where(r => ids.Contains(r.CheckId)).OrderBy(r => r.Id).ToListAsync();
            object? FiscalOf(int checkId, string kind)
            {
                var mine = receipts.Where(r => r.CheckId == checkId && r.Kind == kind).ToList();
                var r = mine.LastOrDefault(x => x.Status == FiscalStatuses.Done) ?? mine.LastOrDefault();
                return r == null ? null : new { r.Status, r.Error, r.FiscalDocumentNumber };
            }
            decimal Total(TableCheck c) => c.Status == CheckStatuses.Open ? openTotals.GetValueOrDefault(c.Id) : c.Status == CheckStatuses.Closed ? c.Total : 0;

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
                        checks = mine.Count,
                        revenue = mine.Where(c => c.Status == CheckStatuses.Closed).Sum(c => c.Total),
                    };
                }),
                checks = checks.Select(c => new
                {
                    c.Id,
                    table = c.Table?.Name,
                    c.Label,
                    waiter = names.GetValueOrDefault(c.WaiterId ?? 0),
                    c.WaiterId,
                    c.Status,
                    c.Guests,
                    c.OpenedAt,
                    c.ClosedAt,
                    c.PaymentMethod,
                    total = Total(c),
                    fiscal = FiscalOf(c.Id, FiscalKinds.Sell),
                    fiscalReturn = FiscalOf(c.Id, FiscalKinds.Return),
                })
            });
        }
    }
}
