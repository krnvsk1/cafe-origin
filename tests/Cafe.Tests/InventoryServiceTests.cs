using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Cafe.API.Data;
using Cafe.API.Models;
using Cafe.API.Services;
using Xunit;

namespace Cafe.Tests
{
    public class InventoryServiceTests
    {
        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"inv-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static InventoryService Service(ApplicationDbContext db) =>
            new(db, NullLogger<InventoryService>.Instance);

        private static async Task<StockItem> Item(ApplicationDbContext db, string name, string kind = StockItemKinds.Raw, string unit = StockUnits.Kg)
        {
            var item = new StockItem { Name = name, Kind = kind, Unit = unit };
            db.StockItems.Add(item);
            await db.SaveChangesAsync();
            return item;
        }

        private static async Task<StockDocument> Receipt(ApplicationDbContext db, InventoryService svc, params (StockItem item, decimal qty, decimal price)[] lines)
        {
            var doc = new StockDocument { Type = StockDocumentTypes.Receipt, Number = "Н-1" };
            foreach (var (item, qty, price) in lines)
                doc.Lines.Add(new StockDocumentLine { StockItemId = item.Id, Quantity = qty, Price = price, Sum = qty * price });
            db.StockDocuments.Add(doc);
            await db.SaveChangesAsync();
            await svc.PostDocumentAsync(doc);
            await db.SaveChangesAsync();
            return doc;
        }

        [Fact]
        public async Task Receipt_IncreasesQuantity_AndComputesWeightedAverageCost()
        {
            using var db = NewContext();
            var svc = Service(db);
            var chicken = await Item(db, "Курица");

            await Receipt(db, svc, (chicken, 10m, 300m));
            await Receipt(db, svc, (chicken, 5m, 360m));

            Assert.Equal(15m, chicken.Quantity);
            Assert.Equal(320m, chicken.AvgCost); // (10·300 + 5·360) / 15
            Assert.Equal(360m, chicken.LastPrice);
            Assert.Equal(2, await db.StockMovements.CountAsync(m => m.StockItemId == chicken.Id));
        }

        [Fact]
        public async Task CancelReceipt_RestoresQuantityAndAverageCost()
        {
            using var db = NewContext();
            var svc = Service(db);
            var chicken = await Item(db, "Курица");

            await Receipt(db, svc, (chicken, 10m, 300m));
            var second = await Receipt(db, svc, (chicken, 5m, 360m));

            await svc.CancelDocumentAsync(second);
            await db.SaveChangesAsync();

            Assert.Equal(10m, chicken.Quantity);
            Assert.Equal(300m, chicken.AvgCost);
            Assert.Equal(StockDocumentStatuses.Cancelled, second.Status);
            await Assert.ThrowsAsync<InventoryException>(() => svc.CancelDocumentAsync(second));
        }

        [Fact]
        public async Task WriteOff_UsesAverageCost_AndCanBeCancelled()
        {
            using var db = NewContext();
            var svc = Service(db);
            var tomato = await Item(db, "Томаты");
            await Receipt(db, svc, (tomato, 4m, 200m));

            var doc = new StockDocument { Type = StockDocumentTypes.WriteOff, Reason = "spoilage" };
            doc.Lines.Add(new StockDocumentLine { StockItemId = tomato.Id, Quantity = 1.5m });
            db.StockDocuments.Add(doc);
            await db.SaveChangesAsync();

            await svc.PostDocumentAsync(doc);
            await db.SaveChangesAsync();

            Assert.Equal(2.5m, tomato.Quantity);
            Assert.Equal(300m, doc.Total);
            Assert.Equal(200m, doc.Lines.Single().Price);

            await svc.CancelDocumentAsync(doc);
            await db.SaveChangesAsync();
            Assert.Equal(4m, tomato.Quantity);
            Assert.Equal(200m, tomato.AvgCost);
        }

        [Fact]
        public async Task Inventory_SetsActualQuantity_AndRecordsDifference()
        {
            using var db = NewContext();
            var svc = Service(db);
            var lavash = await Item(db, "Лаваш", unit: StockUnits.Piece);
            await Receipt(db, svc, (lavash, 100m, 12m));

            var doc = new StockDocument { Type = StockDocumentTypes.Inventory };
            doc.Lines.Add(new StockDocumentLine { StockItemId = lavash.Id, Quantity = 93m });
            db.StockDocuments.Add(doc);
            await db.SaveChangesAsync();

            await svc.PostDocumentAsync(doc);
            await db.SaveChangesAsync();

            var line = doc.Lines.Single();
            Assert.Equal(93m, lavash.Quantity);
            Assert.Equal(100m, line.ExpectedQuantity);
            Assert.Equal(-84m, line.Sum); // −7 шт по 12 ₽
        }

        [Fact]
        public async Task SemiFinishedItems_CannotBeReceived()
        {
            using var db = NewContext();
            var svc = Service(db);
            var sauce = await Item(db, "Соус фирменный", StockItemKinds.Semi);

            var doc = new StockDocument { Type = StockDocumentTypes.Receipt };
            doc.Lines.Add(new StockDocumentLine { StockItemId = sauce.Id, Quantity = 1, Price = 100, Sum = 100 });
            db.StockDocuments.Add(doc);
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<InventoryException>(() => svc.PostDocumentAsync(doc));
        }

        [Fact]
        public void Calculator_ExpandsSemiFinished_ToRawIngredients_AndComputesCost()
        {
            var mayo = new StockItem { Id = 1, Name = "Майонез", Kind = StockItemKinds.Raw, AvgCost = 200m };
            var garlic = new StockItem { Id = 2, Name = "Чеснок", Kind = StockItemKinds.Raw, AvgCost = 400m };
            var sauce = new StockItem { Id = 3, Name = "Соус", Kind = StockItemKinds.Semi };
            var chicken = new StockItem { Id = 4, Name = "Курица", Kind = StockItemKinds.Raw, AvgCost = 300m };

            // 1 кг соуса = 0.9 кг майонеза + 0.1 кг чеснока → 220 ₽/кг
            var sauceCard = new TechCard { Id = 1, StockItemId = 3, YieldQuantity = 1m };
            sauceCard.Lines.Add(new TechCardLine { StockItemId = 1, Gross = 0.9m });
            sauceCard.Lines.Add(new TechCardLine { StockItemId = 2, Gross = 0.1m });

            // Шаурма = 0.15 кг курицы + 0.05 кг соуса
            var dishCard = new TechCard { Id = 2, DishId = 10 };
            dishCard.Lines.Add(new TechCardLine { StockItemId = 4, Gross = 0.15m });
            dishCard.Lines.Add(new TechCardLine { StockItemId = 3, Gross = 0.05m });

            var calc = new TechCardCalculator(new[] { mayo, garlic, sauce, chicken }, new[] { sauceCard, dishCard });

            Assert.Equal(220m, calc.UnitCost(3));
            Assert.Equal(56m, calc.CardCost(dishCard)); // 0.15·300 + 0.05·220

            var raw = calc.ExpandToRaw(dishCard, 2);
            Assert.Equal(0.3m, raw[4]);
            Assert.Equal(0.09m, raw[1]);
            Assert.Equal(0.01m, raw[2]);
            Assert.False(raw.ContainsKey(3));

            // Вариант без своей карты берёт карту карточки
            Assert.Same(dishCard, calc.ForDish(11, parentId: 10));
            Assert.True(calc.CreatesCycle(3, new[] { 3 }));
            Assert.False(calc.CreatesCycle(3, new[] { 1, 2 }));
        }

        [Fact]
        public async Task OrderInWork_DeductsByTechCards_IncludingAddons_AndCancelReturnsStock()
        {
            using var db = NewContext();
            var svc = Service(db);

            var chicken = await Item(db, "Курица");
            var lavash = await Item(db, "Лаваш", unit: StockUnits.Piece);
            var cheese = await Item(db, "Сыр");
            await Receipt(db, svc, (chicken, 10m, 300m), (lavash, 50m, 10m), (cheese, 2m, 800m));

            var card = new Dish { Name = "Шаурма", Price = 0, Category = "Шаурма" };
            db.Dishes.Add(card);
            await db.SaveChangesAsync();
            var big = new Dish { Name = "Большая", Price = 350, ParentId = card.Id, Category = "Шаурма" };
            db.Dishes.Add(big);

            var addonCategory = new AddonCategory { Name = "Добавки" };
            db.AddonCategories.Add(addonCategory);
            await db.SaveChangesAsync();
            var extraCheese = new Addon { Name = "Сыр", Price = 50, AddonCategoryId = addonCategory.Id };
            db.Addons.Add(extraCheese);
            await db.SaveChangesAsync();

            // Карта на карточку целиком — вариант «Большая» её наследует
            var dishCard = new TechCard { DishId = card.Id };
            dishCard.Lines.Add(new TechCardLine { StockItemId = chicken.Id, Gross = 0.2m });
            dishCard.Lines.Add(new TechCardLine { StockItemId = lavash.Id, Gross = 1m });
            var addonCard = new TechCard { AddonId = extraCheese.Id };
            addonCard.Lines.Add(new TechCardLine { StockItemId = cheese.Id, Gross = 0.03m });
            db.TechCards.AddRange(dishCard, addonCard);

            var order = new Order { CustomerName = "Гость", Total = 800, Status = OrderStatuses.New };
            var item = new OrderItem { DishId = big.Id, Name = "Шаурма Большая", Quantity = 2, Price = 350 };
            item.SelectedAddons.Add(new OrderItemAddon { AddonId = extraCheese.Id, AddonName = "Сыр", AddonCategoryId = addonCategory.Id, Price = 50, Quantity = 1 });
            order.OrderItems.Add(item);
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            // Новый → Принят: повар принял, но ещё не готовит — ничего не списываем
            order.Status = OrderStatuses.Accepted;
            await svc.OnOrderStatusChangedAsync(order, OrderStatuses.New, OrderStatuses.Accepted);
            await db.SaveChangesAsync();

            Assert.Null(order.StockDeductedAt);
            Assert.Equal(10m, chicken.Quantity);

            // Принят → Готовится: списание
            order.Status = OrderStatuses.Preparing;
            await svc.OnOrderStatusChangedAsync(order, OrderStatuses.Accepted, OrderStatuses.Preparing);
            await db.SaveChangesAsync();

            Assert.NotNull(order.StockDeductedAt);
            Assert.Equal(9.6m, chicken.Quantity);   // 10 − 2·0.2
            Assert.Equal(48m, lavash.Quantity);     // 50 − 2
            Assert.Equal(1.94m, cheese.Quantity);   // 2 − 2·0.03

            // Повторная смена статуса не списывает второй раз
            await svc.OnOrderStatusChangedAsync(order, OrderStatuses.Preparing, OrderStatuses.Ready);
            await db.SaveChangesAsync();
            Assert.Equal(9.6m, chicken.Quantity);

            // Отмена: всё возвращается
            await svc.OnOrderStatusChangedAsync(order, OrderStatuses.Ready, OrderStatuses.Cancelled);
            await db.SaveChangesAsync();

            Assert.Null(order.StockDeductedAt);
            Assert.Equal(10m, chicken.Quantity);
            Assert.Equal(50m, lavash.Quantity);
            Assert.Equal(2m, cheese.Quantity);
            Assert.Equal(300m, chicken.AvgCost);
        }

        [Fact]
        public void AverageCost_ResetsToReceiptPrice_WhenStockWasNegative()
        {
            Assert.Equal(400m, InventoryService.NewAverageCost(-2m, 300m, 5m, 400m, affectsAvg: true));
            Assert.Equal(300m, InventoryService.NewAverageCost(5m, 300m, -2m, 300m, affectsAvg: false));
        }
    }
}
