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
                .Include(o => o.OrderItems)
                .ToListAsync();

            var current = orders.Where(o => o.CreatedAt.ToUniversalTime() >= periodStart).ToList();
            var previous = orders.Where(o => o.CreatedAt.ToUniversalTime() < periodStart).ToList();

            static bool Counts(Cafe.API.Models.Order o) => o.Status != OrderStatuses.Cancelled;

            var valid = current.Where(Counts).ToList();
            var prevValid = previous.Where(Counts).ToList();

            decimal Revenue(IEnumerable<Cafe.API.Models.Order> list) => list.Sum(o => o.Total);
            DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc.ToUniversalTime(), DateTimeKind.Utc), tz);

            var revenue = Revenue(valid);
            var prevRevenue = Revenue(prevValid);

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
                return new { Date = date.ToString("yyyy-MM-dd"), Revenue = Revenue(dayOrders), Orders = dayOrders.Count };
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
                .OrderByDescending(x => x.Orders)
                .ToList();

            var points = await _context.UserPointsTransactions.AsNoTracking()
                .Where(t => t.CreatedAt >= periodStart && t.CreatedAt < rangeEnd)
                .GroupBy(t => t.Type)
                .Select(g => new { Type = g.Key, Amount = g.Sum(t => t.Amount) })
                .ToListAsync();

            var reviews = await _context.OrderReviews.AsNoTracking()
                .Where(r => r.CreatedAt >= periodStart && r.CreatedAt < rangeEnd)
                .Select(r => r.Rating)
                .ToListAsync();

            double? Change(decimal now, decimal before) => before == 0 ? null : (double)Math.Round((now - before) / before * 100, 1);

            return Ok(new
            {
                From = fromDate.ToString("yyyy-MM-dd"),
                To = toDate.ToString("yyyy-MM-dd"),
                Summary = new
                {
                    Revenue = revenue,
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
                    AverageRating = reviews.Count > 0 ? Math.Round(reviews.Average(), 2) : (double?)null
                },
                ByDay = byDay,
                ByHour = byHour,
                TopItems = topItems,
                DeliveryTypes = deliveryTypes,
                PaymentMethods = paymentMethods
            });
        }
    }
}
