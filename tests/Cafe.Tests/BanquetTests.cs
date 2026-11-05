using Cafe.API.Models;
using Cafe.API.Services;
using Cafe.API.Services.Fiscal;
using Xunit;

namespace Cafe.Tests
{
    public class BanquetTests
    {
        private static List<BanquetItem> Items() => new()
        {
            new BanquetItem { Id = 1, Name = "Шашлык", Price = 450, Quantity = 10 },
            new BanquetItem { Id = 2, Name = "Салат", Price = 333.33m, Quantity = 3 },
        };

        [Fact]
        public void Totals_DiscountOnDishes_ServicePercentOfNet_PrepaidAndDue()
        {
            var b = new Banquet { DiscountType = DiscountTypes.Percent, DiscountValue = 10, ServiceType = DiscountTypes.Percent, ServiceValue = 10 };
            var pays = new[]
            {
                new BanquetPayment { Kind = BanquetPaymentKinds.Prepay, Amount = 2000 },
                new BanquetPayment { Kind = BanquetPaymentKinds.Prepay, Amount = 500, RefundedAt = DateTime.UtcNow },
            };
            var t = BanquetService.Totals(b, Items(), pays);
            Assert.Equal(5499.99m, t.Dishes);
            Assert.Equal(550m, t.Discount);
            Assert.Equal(495m, t.Service);
            Assert.Equal(5444.99m, t.Total);
            Assert.Equal(2000m, t.Prepaid);
            Assert.Equal(3444.99m, t.Due);
        }

        [Fact]
        public void FinalLines_SumEqualsFiscalSum_WithServiceLine()
        {
            var b = new Banquet { ServiceType = DiscountTypes.Amount, ServiceValue = 1000 };
            var t = BanquetService.Totals(b, Items(), Array.Empty<BanquetPayment>());
            var lines = BanquetService.FinalLines(Items(), t, t.Total);
            Assert.Equal(t.Total, lines.Sum(l => l.Amount));
            Assert.Contains(lines, l => l.IsService && l.Name == "Обслуживание");

            // Остаток на сотрудника: в чек — только оплаченное
            var part = BanquetService.FinalLines(Items(), t, 3000);
            Assert.Equal(3000m, part.Sum(l => l.Amount));
        }

        [Fact]
        public void FinalReceipt_MixedPayments_AdvanceLinesStillSupported()
        {
            var o = new FiscalOptions();
            var b = new Banquet { Id = 7, EventAt = new DateTime(2026, 12, 20, 15, 0, 0, DateTimeKind.Utc) };
            var adv = FiscalTasks.Receipt(FiscalKinds.Sell, o, BanquetService.PrepayLines(b, 5000), "card", "Иванова", null);
            Assert.Equal("advance", adv["items"]![0]!["paymentMethod"]!.GetValue<string>());
            Assert.Equal("payment", adv["items"]![0]!["paymentObject"]!.GetValue<string>());

            var lines = new List<ReceiptLine> { new("Шашлык", 8000, 1, 8000, false) };
            var fin = FiscalTasks.Receipt(FiscalKinds.Sell, o, lines, "cash", "Иванова", null, null, new List<(string, decimal)> { ("cash", 5000), ("electronically", 3000) });
            Assert.Equal("cash", fin["payments"]![0]!["type"]!.GetValue<string>());
            Assert.Equal(3000m, fin["payments"]![1]!["sum"]!.GetValue<decimal>());
            Assert.Equal("fullPayment", fin["items"]![0]!["paymentMethod"]!.GetValue<string>());
        }

        [Fact]
        public void Discount_PercentAndAmount_NotMoreThanSum()
        {
            Assert.Equal(150m, DiscountTypes.Of(DiscountTypes.Percent, 10, 1500));
            Assert.Equal(1500m, DiscountTypes.Of(DiscountTypes.Amount, 5000, 1500));
            Assert.Equal(0m, DiscountTypes.Of(null, 10, 1500));
        }
    }
}
