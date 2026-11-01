using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Cafe.API.Data;
using Cafe.API.Models;
using Cafe.API.Services;
using Xunit;

namespace Cafe.Tests
{
    public class InventorySemiTests
    {
        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"inv-semi-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        [Fact]
        public async Task Inventory_CountsRawInsideSemiFinished()
        {
            using var db = NewContext();
            var svc = new InventoryService(db, NullLogger<InventoryService>.Instance);
            var pork = new StockItem { Name = "Свинина", Kind = StockItemKinds.Raw, Unit = StockUnits.Kg, Quantity = 10, AvgCost = 500 };
            var onion = new StockItem { Name = "Лук", Kind = StockItemKinds.Raw, Unit = StockUnits.Kg, Quantity = 3, AvgCost = 40 };
            var marinade = new StockItem { Name = "Шея в маринаде", Kind = StockItemKinds.Semi, Unit = StockUnits.Kg };
            db.StockItems.AddRange(pork, onion, marinade);
            await db.SaveChangesAsync();

            // Выход 1 кг маринованного мяса: 0,8 кг свинины и 0,25 кг лука
            var card = new TechCard { StockItemId = marinade.Id, YieldQuantity = 1 };
            card.Lines.Add(new TechCardLine { StockItemId = pork.Id, Gross = 0.8m });
            card.Lines.Add(new TechCardLine { StockItemId = onion.Id, Gross = 0.25m });
            db.TechCards.Add(card);

            // Пересчитали: 2 кг свинины лежит как есть, 5 кг мяса в маринаде; лук отдельно не считали
            var doc = new StockDocument { Type = StockDocumentTypes.Inventory };
            doc.Lines.Add(new StockDocumentLine { StockItemId = pork.Id, Quantity = 2, SortOrder = 0 });
            doc.Lines.Add(new StockDocumentLine { StockItemId = marinade.Id, Quantity = 5, SortOrder = 1 });
            db.StockDocuments.Add(doc);
            await db.SaveChangesAsync();

            await svc.PostDocumentAsync(doc);
            await db.SaveChangesAsync();

            Assert.Equal(6m, pork.Quantity);      // 2 + 5 × 0,8
            Assert.Equal(1.25m, onion.Quantity);  // весь лук — в маринаде: 5 × 0,25
            var porkLine = doc.Lines.Single(l => l.StockItemId == pork.Id);
            Assert.Equal(4m, porkLine.SemiQuantity);
            Assert.Equal(10m, porkLine.ExpectedQuantity);
            var onionLine = doc.Lines.Single(l => l.StockItemId == onion.Id);
            Assert.Equal(0m, onionLine.Quantity);
            Assert.Equal(1.25m, onionLine.SemiQuantity);
            Assert.Equal(0m, marinade.Quantity);  // сам п/ф на складе не хранится

            // Отмена документа возвращает остатки
            await svc.CancelDocumentAsync(doc);
            await db.SaveChangesAsync();
            Assert.Equal(10m, pork.Quantity);
            Assert.Equal(3m, onion.Quantity);
        }

        [Fact]
        public async Task SemiInReceipt_IsRejected()
        {
            using var db = NewContext();
            var svc = new InventoryService(db, NullLogger<InventoryService>.Instance);
            var marinade = new StockItem { Name = "Шея в маринаде", Kind = StockItemKinds.Semi, Unit = StockUnits.Kg };
            db.StockItems.Add(marinade);
            var doc = new StockDocument { Type = StockDocumentTypes.Receipt };
            doc.Lines.Add(new StockDocumentLine { StockItem = marinade, Quantity = 1, Price = 100, Sum = 100 });
            db.StockDocuments.Add(doc);
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<InventoryException>(() => svc.PostDocumentAsync(doc));
        }
    }
}
