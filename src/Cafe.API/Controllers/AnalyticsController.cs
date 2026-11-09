using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Cafe.API.Data;
using Cafe.API.Options;
using Cafe.API.Services;
using Cafe.API.Authorization;

namespace Cafe.API.Controllers
{
    /// <summary>
    /// Аналитика для владельца: выручка, средний чек, популярные позиции, промокоды и баллы
    /// </summary>
    [ApiController]
    [Route("api/analytics")]
    [HasPermission(Permissions.Analytics)]
    public class AnalyticsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly RestaurantOptions _restaurant;

        public AnalyticsController(ApplicationDbContext context, IOptions<RestaurantOptions> restaurant)
        {
            _context = context;
            _restaurant = restaurant.Value;
        }

        // GET: api/analytics?from=2026-10-01&to=2026-10-24 (даты по местному времени заведения, включительно)
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
        {
            var tz = _restaurant.GetTimeZone();
            var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
            var toDate = to ?? todayLocal;
            var fromDate = from ?? toDate.AddDays(-29);
            if (fromDate > toDate) (fromDate, toDate) = (toDate, fromDate);
            if (toDate.DayNumber - fromDate.DayNumber > 366) fromDate = toDate.AddDays(-366);

            var days = toDate.DayNumber - fromDate.DayNumber + 1;
            var prevTo = fromDate.AddDays(-1);
            var prevFrom = prevTo.AddDays(-(days - 1));

            DateTime StartUtc(DateOnly d) => TimeZoneInfo.ConvertTimeToUtc(d.ToDateTime(TimeOnly.MinValue), tz);

            var rangeStart = StartUtc(prevFrom);
            var rangeEnd = StartUtc(toDate.AddDays(1));
            var periodStart = StartUtc(fromDate);

            // Учитываем все заказы, кроме неоплаченных онлайн-заказов
            var orders = await _context.Orders.AsNoTracking()
                .Where(o => o.CreatedAt >= rangeStart && o.CreatedAt < rangeEnd)
                .Where(o => o.PaymentMethod != "online" || o.PaymentStatus == "paid" || o.PaymentStatus == "refunded")
                // Закрытые на сотрудника —  (без оплаты, к удержанию с сотрудника)
                .Where(o => o.PaymentMethod != "staff")
                // Заказы кухни банкета — выручка считается по самим банкетам (с обслуживанием, скидкой и предоплатами)
                .Where(o => o.Source != "banquet")
                .Include(o => o.OrderItems)
                .ToListAsync();

            // Возвраты: деньги вернули гостю — ; показываются отдельно
            var refunds = await Cafe.API.Services.Refunds.LoadAsync(_context);
            orders = orders.Where(o => !refunds.IsRefunded(o)).ToList();
            var refundsNow = refunds.Between(periodStart, rangeEnd);
            var refundsBefore = refunds.Between(rangeStart, periodStart);

            var current = orders.Where(o => o.CreatedAt.ToUniversalTime() >= periodStart).ToList();
            var previous = orders.Where(o => o.CreatedAt.ToUniversalTime() < periodStart).ToList();

            static bool Counts(Cafe.API.Models.Order o) => o.Status != OrderStatuses.Cancelled;

            var valid = current.Where(Counts).ToList();
            var prevValid = previous.Where(Counts).ToList();

            decimal Revenue(IEnumerable<Cafe.API.Models.Order> list) => list.Sum(o => o.Total);
            DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc.ToUniversalTime(), DateTimeKind.Utc), tz);

            // Банкеты: деньги (предоплаты и расчёт наличными/картой) по закрытым банкетам — в день закрытия
            var banquets = await _context.Banquets.AsNoTracking()
                .Where(b => b.Status == Cafe.API.Models.BanquetStatuses.Closed && b.ClosedAt >= rangeStart && b.ClosedAt < rangeEnd)
                .Select(b => new { b.Id, b.ClosedAt, b.Total }).ToListAsync();
            var banquetIds = banquets.Select(b => b.Id).ToList();
            var banquetPays = await _context.BanquetPayments.AsNoTracking()
                .Where(p => banquetIds.Contains(p.BanquetId) && p.RefundedAt == null).ToListAsync();
            decimal BanquetMoney(int id) => banquetPays.Where(p => p.BanquetId == id && (p.Method == "cash" || p.Method == "card")).Sum(p => p.Amount);
            var banquetNow = banquets.Where(b => b.ClosedAt >= periodStart).ToList();
            var banquetRevenue = banquetNow.Sum(b => BanquetMoney(b.Id));
            var banquetPrevRevenue = banquets.Where(b => b.ClosedAt < periodStart).Sum(b => BanquetMoney(b.Id));

            var revenue = Revenue(valid) + banquetRevenue;
            var prevRevenue = Revenue(prevValid) + banquetPrevRevenue;

            // Клиенты: по пользователю, для гостей — по телефону
            string CustomerKey(Cafe.API.Models.Order o) => o.UserId?.ToString() ?? $"tel:{o.Phone}";
            var customers = valid.Select(CustomerKey).Distinct().ToList();
            var earlierCustomers = await _context.Orders.AsNoTracking()
                .Where(o => o.CreatedAt < periodStart && o.Status != OrderStatuses.Cancelled)
                .Select(o => o.UserId != null ? o.UserId.Value.ToString() : "tel:" + o.Phone)
                .Distinct()
                .ToListAsync();
            var earlierSet = earlierCustomers.ToHashSet();
            var newCustomers = customers.Count(c => !earlierSet.Contains(c));
            var repeatCustomers = valid.GroupBy(CustomerKey).Count(g => g.Count() > 1);

            var byDay = Enumerable.Range(0, days).Select(i =>
            {
                var date = fromDate.AddDays(i);
                var dayOrders = valid.Where(o => DateOnly.FromDateTime(Local(o.CreatedAt)) == date).ToList();
                var dayBanquets = banquetNow.Where(b => b.ClosedAt != null && DateOnly.FromDateTime(Local(b.ClosedAt.Value)) == date).Sum(b => BanquetMoney(b.Id));
                return new { Date = date.ToString("yyyy-MM-dd"), Revenue = Revenue(dayOrders) + dayBanquets, Orders = dayOrders.Count };
            }).ToList();

            var byHour = Enumerable.Range(0, 24).Select(h => new
            {
                Hour = h,
                Orders = valid.Count(o => Local(o.CreatedAt).Hour == h)
            }).ToList();

            var topItems = valid
                .SelectMany(o => o.OrderItems)
                .GroupBy(i => i.Weight.HasValue ? Weighing.StripWeight(i.Name) : i.Name)
                .Select(g => new { Name = g.Key, Quantity = g.Sum(i => i.Quantity), Revenue = g.Sum(i => i.Price * i.Quantity) })
                .OrderByDescending(x => x.Quantity)
                .Take(10)
                .ToList();

            var deliveryTypes = valid.GroupBy(o => o.DeliveryType)
                .Select(g => new { Type = g.Key, Orders = g.Count(), Revenue = g.Sum(o => o.Total) })
                .OrderByDescending(x => x.Orders)
                .ToList();

            var paymentMethods = valid.GroupBy(o => o.PaymentMethod)
                .Select(g => new { Method = g.Key, Orders = g.Count(), Revenue = g.Sum(o => o.Total) })
                .ToList();
            if (banquetNow.Count > 0) paymentMethods.Add(new { Method = "banquet", Orders = banquetNow.Count, Revenue = banquetRevenue });
            paymentMethods = paymentMethods.OrderByDescending(x => x.Orders).ToList();

            var points = await _context.UserPointsTransactions.AsNoTracking()
                .Where(t => t.CreatedAt >= periodStart && t.CreatedAt < rangeEnd)
                .GroupBy(t => t.Type)
                .Select(g => new { Type = g.Key, Amount = g.Sum(t => t.Amount) })
                .ToListAsync();

            var reviews = await _context.OrderReviews.AsNoTracking()
                .Where(r => r.CreatedAt >= periodStart && r.CreatedAt < rangeEnd)
                .Select(r => r.Rating)
                .ToListAsync();

            // Закрыто на сотрудников (без оплаты и без чека): , сумма к удержанию с сотрудников
            var staffChecks = await _context.TableChecks.AsNoTracking()
                .Where(c => c.Status == Cafe.API.Models.CheckStatuses.Closed && c.PaymentMethod == Cafe.API.Models.CheckPayments.Staff
                            && c.RefundedAt == null && c.ClosedAt >= rangeStart && c.ClosedAt < rangeEnd)
                .Select(c => new { c.ClosedAt, c.Total, c.StaffUserId })
                .ToListAsync();
            // и остаток банкетов, закрытый на сотрудника
            staffChecks.AddRange(await _context.BanquetPayments.AsNoTracking()
                .Where(p => p.Method == "staff" && p.RefundedAt == null && p.CreatedAt >= rangeStart && p.CreatedAt < rangeEnd)
                .Select(p => new { ClosedAt = (DateTime?)p.CreatedAt, Total = p.Amount, p.StaffUserId })
                .ToListAsync());
            // и заказы с сайта, закрытые на сотрудника
            staffChecks.AddRange(await _context.Orders.AsNoTracking()
                .Where(o => o.PaymentMethod == Cafe.API.Models.CheckPayments.Staff && o.CheckId == null && o.Status != OrderStatuses.Cancelled
                            && o.ClosedAt >= rangeStart && o.ClosedAt < rangeEnd)
                .Select(o => new { o.ClosedAt, o.Total, o.StaffUserId })
                .ToListAsync());
            var staffNow = staffChecks.Where(c => c.ClosedAt >= periodStart).ToList();
            var staffBefore = staffChecks.Where(c => c.ClosedAt < periodStart).ToList();
            var staffNames = await WaiterController.StaffNamesAsync(_context, staffNow.Select(c => c.StaffUserId));
            var staffByPerson = staffNow.GroupBy(c => c.StaffUserId ?? 0)
                .Select(g => new { Name = staffNames.GetValueOrDefault(g.Key) ?? "—", Checks = g.Count(), Total = g.Sum(c => c.Total) })
                .OrderByDescending(x => x.Total)
                .ToList();

            double? Change(decimal now, decimal before) => before == 0 ? null : (double)Math.Round((now - before) / before * 100, 1);

            return Ok(new
            {
                From = fromDate.ToString("yyyy-MM-dd"),
                To = toDate.ToString("yyyy-MM-dd"),
                Summary = new
                {
                    Revenue = revenue,
                    BanquetRevenue = banquetRevenue,
                    Banquets = banquetNow.Count,
                    RevenueChange = Change(revenue, prevRevenue),
                    Orders = valid.Count,
                    OrdersChange = Change(valid.Count, prevValid.Count),
                    AverageCheck = valid.Count > 0 ? Math.Round(revenue / valid.Count, 0) : 0,
                    AverageCheckChange = Change(
                        valid.Count > 0 ? revenue / valid.Count : 0,
                        prevValid.Count > 0 ? prevRevenue / prevValid.Count : 0),
                    Cancelled = current.Count(o => o.Status == OrderStatuses.Cancelled),
                    Customers = customers.Count,
                    NewCustomers = newCustomers,
                    RepeatCustomers = repeatCustomers,
                    DeliveryRevenue = valid.Sum(o => o.DeliveryPrice),
                    PromoOrders = valid.Count(o => o.PromoCodeId != null),
                    PromoDiscount = valid.Sum(o => o.DiscountAmount),
                    PointsSpent = -points.Where(p => p.Type == "spent").Sum(p => p.Amount),
                    PointsEarned = points.Where(p => p.Type is "earned" or "referral" or "admin_grant").Sum(p => p.Amount),
                    ReviewsCount = reviews.Count,
                    AverageRating = reviews.Count > 0 ? Math.Round(reviews.Average(), 2) : (double?)null,
                    StaffTotal = staffNow.Sum(c => c.Total),
                    StaffTotalChange = Change(staffNow.Sum(c => c.Total), staffBefore.Sum(c => c.Total)),
                    StaffChecks = staffNow.Count,
                    Refunds = refundsNow.Count,
                    RefundsTotal = refundsNow.Sum(r => r.Amount),
                    RefundsTotalChange = Change(refundsNow.Sum(r => r.Amount), refundsBefore.Sum(r => r.Amount))
                },
                StaffByPerson = staffByPerson,
                ByDay = byDay,
                ByHour = byHour,
                TopItems = topItems,
                DeliveryTypes = deliveryTypes,
                PaymentMethods = paymentMethods
            });
        }
    }
}
