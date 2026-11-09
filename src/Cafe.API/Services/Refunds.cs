using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Models;

namespace Cafe.API.Services
{
    /// <summary>Возврат денег гостю: чек возврата по счёту или заказу с сайта (последний пробитый — возврат)</summary>
    public record RefundEntry(int? CheckId, int? OrderId, DateTime At, decimal Amount, string? Method);

    /// <summary>
    /// Возвращённые счета и заказы: пробитых чеков возврата не меньше, чем продажи (смена оплаты — возврат и новая продажа — не возврат).
    /// Их деньги не выручка: в дашборде, аналитике и сменах они идут отдельной строкой «Возвраты».
    /// </summary>
    public class Refunds
    {
        public HashSet<int> Checks { get; } = new();
        public HashSet<int> Orders { get; } = new();
        public List<RefundEntry> Entries { get; } = new();

        public bool IsRefunded(Order o) =>
            Orders.Contains(o.Id) || (o.CheckId is int c && Checks.Contains(c))
            || (o.CheckId == null && o.OrderItems.Count > 0 && o.OrderItems.All(i => i.CheckId is int ic && Checks.Contains(ic)));

        public static async Task<Refunds> LoadAsync(ApplicationDbContext db, CancellationToken ct = default)
        {
            var rows = await db.FiscalReceipts.AsNoTracking()
                .Where(r => r.Status == FiscalStatuses.Done && r.BanquetId == null && (r.CheckId != null || r.OrderId != null))
                .Select(r => new { r.CheckId, r.OrderId, r.Kind, r.Total, r.CreatedAt, r.PaymentMethod })
                .ToListAsync(ct);
            var result = new Refunds();
            foreach (var g in rows.GroupBy(r => r.CheckId != null ? (Check: r.CheckId, Order: (int?)null) : (Check: (int?)null, Order: r.OrderId)))
            {
                var sells = g.Count(r => r.Kind == FiscalKinds.Sell);
                var returns = g.Where(r => r.Kind == FiscalKinds.Return).OrderBy(r => r.CreatedAt).ToList();
                if (returns.Count == 0 || returns.Count < sells) continue;
                var last = returns[^1];
                if (g.Key.Check is int c) result.Checks.Add(c); else if (g.Key.Order is int o) result.Orders.Add(o);
                result.Entries.Add(new RefundEntry(g.Key.Check, g.Key.Order, last.CreatedAt, last.Total, last.PaymentMethod));
            }
            return result;
        }

        public List<RefundEntry> Between(DateTime fromUtc, DateTime toUtc) => Entries.Where(e => e.At >= fromUtc && e.At < toUtc).ToList();
    }
}
