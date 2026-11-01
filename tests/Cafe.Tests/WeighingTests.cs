using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Cafe.API.Data;
using Cafe.API.Models;
using Cafe.API.Services;
using Xunit;

namespace Cafe.Tests
{
    public class WeighingTests
    {
        [Theory]
        [InlineData(300, 350, 1050)]
        [InlineData(300, 100, 300)]
        [InlineData(450, 250, 1125)]
        [InlineData(199.99, 333, 665.97)]
        public void PriceFor_IsPer100Grams(decimal per100, int grams, decimal expected) =>
            Assert.Equal(expected, Weighing.PriceFor(per100, grams));

        [Fact]
        public void NameWith_AddsWeight_AndStripRemovesIt()
        {
            Assert.Equal("Свиная шея · 350 г", Weighing.NameWith("Свиная шея", 350));
            Assert.Equal("Свиная шея · 1,5 кг", Weighing.NameWith("Свиная шея", 1500));
            Assert.Equal("Свиная шея", Weighing.StripWeight("Свиная шея · 350 г"));
            Assert.Equal("Свиная шея", Weighing.StripWeight("Свиная шея · 1,5 кг"));
            Assert.Equal("Чай · мята", Weighing.StripWeight("Чай · мята"));
            Assert.True(Weighing.NameWith(new string('я', 120), 350).Length <= 100);
        }

        [Fact]
        public void Validate_RequiresWeightOnlyForWeighted()
        {
            Assert.Null(Weighing.Validate(false, null, "Пиво"));
            Assert.NotNull(Weighing.Validate(false, 300, "Пиво"));
            Assert.NotNull(Weighing.Validate(true, null, "Шея"));
            Assert.NotNull(Weighing.Validate(true, 5, "Шея"));
            Assert.Null(Weighing.Validate(true, 350, "Шея"));
        }

        [Fact]
        public void Portions_ScaleTechCardPer100Grams()
        {
            Assert.Equal(3.5m, Weighing.Portions(1, 350));
            Assert.Equal(7m, Weighing.Portions(2, 350));
            Assert.Equal(2m, Weighing.Portions(2, null));
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"weigh-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        [Fact]
        public async Task WeightedItem_DeductsByWeight_AndAdjustsOnCorrection()
        {
            using var db = NewContext();
            var svc = new InventoryService(db, NullLogger<InventoryService>.Instance);
            var pork = new StockItem { Name = "Свинина", Kind = StockItemKinds.Raw, Unit = StockUnits.Kg, Quantity = 10 };
            db.StockItems.Add(pork);
            var card = new Dish { Name = "Свиная шея", Price = 300, Category = "Мангал", IsWeighted = true };
            db.Dishes.Add(card);
            await db.SaveChangesAsync();
            var variant = new Dish { Name = "Стандарт", Price = 300, Category = "Мангал", ParentId = card.Id };
            db.Dishes.Add(variant);
            await db.SaveChangesAsync();

            // Техкарта на 100 г готового: 0,13 кг сырого мяса
            var tc = new TechCard { DishId = variant.Id };
            tc.Lines.Add(new TechCardLine { StockItemId = pork.Id, Gross = 0.13m });
            db.TechCards.Add(tc);

            var order = new Order { CustomerName = "Стол 1", Total = 1050, Status = OrderStatuses.New };
            var item = new OrderItem { DishId = variant.Id, Name = "Свиная шея · 350 г", Quantity = 1, Price = 1050, Weight = 350 };
            order.OrderItems.Add(item);
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            order.Status = OrderStatuses.Preparing;
            await svc.OnOrderStatusChangedAsync(order, OrderStatuses.New, OrderStatuses.Preparing);
            await db.SaveChangesAsync();
            Assert.Equal(10m - 0.455m, pork.Quantity);   // 3,5 × 0,13

            // Повар взвесил: 400 г — досписываем 0,5 × 0,13
            await svc.AdjustWeightStockAsync(order, item, 350, 400);
            item.Weight = 400;
            await db.SaveChangesAsync();
            Assert.Equal(10m - 0.52m, pork.Quantity);

            // Отмена заказа возвращает всё, включая досписанное
            await svc.OnOrderStatusChangedAsync(order, OrderStatuses.Preparing, OrderStatuses.Cancelled);
            await db.SaveChangesAsync();
            Assert.Equal(10m, pork.Quantity);
        }
    }
}
