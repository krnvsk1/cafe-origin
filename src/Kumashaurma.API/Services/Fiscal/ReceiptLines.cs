using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Services.Fiscal
{
    /// <summary>Строка чека: Amount = Price × Quantity с точностью до копейки</summary>
    public record ReceiptLine(string Name, decimal Price, decimal Quantity, decimal Amount, bool IsService);

    /// <summary>
    /// Позиции чека по заказу (и для кассы АТОЛ, и для чеков ЮKassa).
    /// Скидка (промокод, баллы) распределяется по блюдам пропорционально сумме — доставка идёт отдельной услугой без скидки.
    /// Сумма строк всегда равна Order.Total.
    /// </summary>
    public static class ReceiptLines
    {
        public const int MaxName = 128;

        public static async Task<List<ReceiptLine>> LoadAsync(ApplicationDbContext db, Order order, CancellationToken ct = default)
        {
            var items = await db.OrderItems.AsNoTracking()
                .Include(i => i.SelectedAddons)
                .Where(i => i.OrderId == order.Id)
                .OrderBy(i => i.Id)
                .ToListAsync(ct);
            return Build(items, order.DeliveryPrice, order.Total);
        }

        public static List<ReceiptLine> Build(IReadOnlyList<OrderItem> items, decimal deliveryPrice, decimal total)
        {
            var raw = items
                .Where(i => i.Quantity > 0)
                .Select(i => (Name: NameOf(i), Unit: i.Price + i.SelectedAddons.Sum(a => a.Price * a.Quantity), Qty: i.Quantity))
                .ToList();

            var delivery = Math.Max(0, deliveryPrice);
            if (delivery > total) delivery = total;
            var goodsTarget = Round(total - delivery);
            var goodsFull = raw.Sum(r => r.Unit * r.Qty);

            // Суммы по строкам после скидки; копейки от округления — на последнюю строку
            var amounts = new List<decimal>();
            decimal acc = 0;
            for (var k = 0; k < raw.Count; k++)
            {
                var full = raw[k].Unit * raw[k].Qty;
                var a = k == raw.Count - 1
                    ? goodsTarget - acc
                    : goodsFull > 0 ? Round(full * goodsTarget / goodsFull) : 0;
                a = Math.Max(0, a);
                amounts.Add(a);
                acc += a;
            }

            var lines = new List<ReceiptLine>();
            for (var k = 0; k < raw.Count; k++)
            {
                var (name, _, qty) = raw[k];
                var amount = amounts[k];
                var unit = Math.Floor(amount / qty * 100) / 100;
                if (unit * qty == amount)
                {
                    lines.Add(new ReceiptLine(name, unit, qty, amount, false));
                }
                else
                {
                    // Сумма не делится на количество без остатка — одна порция отдельной строкой
                    lines.Add(new ReceiptLine(name, unit, qty - 1, unit * (qty - 1), false));
                    var last = amount - unit * (qty - 1);
                    lines.Add(new ReceiptLine(name, last, 1, last, false));
                }
            }
            if (delivery > 0) lines.Add(new ReceiptLine("Доставка", delivery, 1, delivery, true));
            return lines;
        }

        private static string NameOf(OrderItem i)
        {
            var name = i.Name.Trim();
            var addons = i.SelectedAddons
                .Select(a => a.Quantity > 1 ? $"{a.AddonName} ×{a.Quantity}" : a.AddonName)
                .ToList();
            if (addons.Count > 0) name += " + " + string.Join(", ", addons);
            return name.Length <= MaxName ? name : name[..(MaxName - 1)] + "…";
        }

        private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    }
}
