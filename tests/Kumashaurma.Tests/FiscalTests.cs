using Kumashaurma.API.Models;
using Kumashaurma.API.Services.Fiscal;
using Xunit;

namespace Kumashaurma.Tests
{
    public class FiscalTests
    {
        private static OrderItem Item(string name, decimal price, int qty, params (string Name, decimal Price, int Qty)[] addons)
        {
            var i = new OrderItem { Name = name, Price = price, Quantity = qty };
            foreach (var a in addons) i.SelectedAddons.Add(new OrderItemAddon { AddonName = a.Name, Price = a.Price, Quantity = a.Qty });
            return i;
        }

        [Fact]
        public void Lines_WithoutDiscount_MatchItemsAndAddons()
        {
            var lines = ReceiptLines.Build(new[] { Item("Шаурма", 300, 2, ("Сыр", 50, 1)), Item("Морс", 120, 1) }, 0, 820);
            Assert.Equal(2, lines.Count);
            Assert.Equal("Шаурма + Сыр", lines[0].Name);
            Assert.Equal(350, lines[0].Price);
            Assert.Equal(2, lines[0].Quantity);
            Assert.Equal(700, lines[0].Amount);
            Assert.Equal(820, lines.Sum(l => l.Amount));
        }

        [Fact]
        public void Lines_DiscountSpreadOverDishes_DeliveryKeptAsService()
        {
            // 3 × 333 = 999 блюд, скидка 100, доставка 150 → итого 1049
            var lines = ReceiptLines.Build(new[] { Item("Шаурма", 333, 3) }, 150, 1049);
            Assert.Equal(1049, lines.Sum(l => l.Amount));
            Assert.All(lines, l => Assert.Equal(l.Amount, l.Price * l.Quantity));
            var delivery = Assert.Single(lines, l => l.IsService);
            Assert.Equal(150, delivery.Amount);
            Assert.Equal(899, lines.Where(l => !l.IsService).Sum(l => l.Amount));
        }

        [Fact]
        public void Lines_NonDivisibleAmount_SplitsLastPortion()
        {
            var lines = ReceiptLines.Build(new[] { Item("Шаурма", 100, 3) }, 0, 200);
            Assert.Equal(2, lines.Count);
            Assert.Equal(200, lines.Sum(l => l.Amount));
            Assert.All(lines, l => Assert.Equal(l.Amount, l.Price * l.Quantity));
        }

        [Fact]
        public void SellTask_HasPaymentAndClient()
        {
            var o = new FiscalOptions { TaxationType = "patent", Vat = "none", CashierInn = "123456789012" };
            var lines = ReceiptLines.Build(new[] { Item("Шаурма", 300, 1) }, 0, 300);
            var task = FiscalTasks.Receipt(FiscalKinds.Sell, o, lines, "card", "Иванов Иван", "+79991234567");
            Assert.Equal("sell", (string?)task["type"]);
            Assert.Equal("patent", (string?)task["taxationType"]);
            Assert.Equal("electronically", (string?)task["payments"]![0]!["type"]);
            Assert.Equal(300m, (decimal)task["payments"]![0]!["sum"]!);
            Assert.Equal("123456789012", (string?)task["operator"]!["vatin"]);
            Assert.Equal("+79991234567", (string?)task["clientInfo"]!["emailOrPhone"]);
            Assert.Equal("none", (string?)task["items"]![0]!["tax"]!["type"]);
        }

        [Fact]
        public void Parse_Ready_FillsReceipt()
        {
            var r = AtolClient.Parse(@"{""results"":[{""errorCode"":0,""errorDescription"":""Ошибок нет"",""status"":""ready"",
                ""result"":{""fiscalParams"":{""fiscalDocumentNumber"":125,""fiscalDocumentSign"":""3825791523"",""fiscalReceiptNumber"":7,
                ""fnNumber"":""9999078900012345"",""shiftNumber"":12,""fiscalDocumentDateTime"":""2026-10-28T20:41:00+03:00""}}}]}");
            Assert.True(r.Ready);
            var receipt = new FiscalReceipt();
            FiscalService.Apply(receipt, r);
            Assert.Equal(FiscalStatuses.Done, receipt.Status);
            Assert.Equal(125, receipt.FiscalDocumentNumber);
            Assert.Equal(12, receipt.ShiftNumber);
            Assert.Equal("3825791523", receipt.FiscalDocumentSign);
        }

        [Fact]
        public void Parse_ErrorAndPending()
        {
            var err = AtolClient.Parse(@"{""results"":[{""errorCode"":68,""errorDescription"":""Смена превысила 24 часа"",""status"":""error""}]}");
            Assert.False(err.Ready);
            Assert.False(err.Pending);
            Assert.Contains("24 часа", err.Error);

            var wait = AtolClient.Parse(@"{""results"":[{""status"":""inProgress""}]}");
            Assert.True(wait.Pending);
            var receipt = new FiscalReceipt();
            FiscalService.Apply(receipt, wait);
            Assert.Equal(FiscalStatuses.Processing, receipt.Status);
        }
    
        [Fact]
        public void Layout_AddsPreAndPostItemsWithPlaceholders()
        {
            var o = new FiscalOptions
            {
                Layout = new ReceiptLayout
                {
                    Header = new() { new LayoutLine { Text = "Заказ № {order} · {type}", Align = "center", Large = true } },
                    Footer = new() { new LayoutLine { Text = "Кассир {cashier}", Align = "left" } },
                    Qr = "https://kumashaurma.ru/review?o={order}"
                }
            };
            var lines = ReceiptLines.Build(new[] { Item("Шаурма", 300, 1) }, 0, 300);
            var vars = FiscalService.Vars(42, "Самовывоз", "Иванов", new DateTime(2026, 10, 28, 18, 0, 0, DateTimeKind.Utc));
            var task = FiscalTasks.Receipt(FiscalKinds.Sell, o, lines, "cash", "Иванов", null, vars);
            Assert.Equal("Заказ № 42 · с собой", (string?)task["preItems"]![0]!["text"]);
            Assert.True((bool)task["preItems"]![0]!["doubleWidth"]!);
            Assert.Equal("Кассир Иванов", (string?)task["postItems"]![0]!["text"]);
            Assert.Equal("barcode", (string?)task["postItems"]![1]!["type"]);
            Assert.Equal("https://kumashaurma.ru/review?o=42", (string?)task["postItems"]![1]!["barcode"]);
        }

        [Fact]
        public void Layout_Validates()
        {
            Assert.Null(ReceiptLayout.Default().Validate());
            Assert.NotNull(new ReceiptLayout { Width = 50 }.Validate());
            Assert.NotNull(new ReceiptLayout { Header = new() { new LayoutLine { Text = new string('x', 70) } } }.Validate());
        }
    }
}
