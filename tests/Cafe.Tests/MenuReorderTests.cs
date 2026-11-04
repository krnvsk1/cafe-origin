using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Cafe.API.Controllers;
using Cafe.API.Data;
using Cafe.API.Models;
using Xunit;

namespace Cafe.Tests
{
    public class MenuReorderTests
    {
        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"reorder-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        [Fact]
        public async Task Reorder_MovesCardWithVariantsToOtherSection()
        {
            using var db = NewContext();
            var tea = new Dish { Name = "Чай", Price = 100, Category = "Напитки", SortOrder = 10 };
            var soup = new Dish { Name = "Борщ", Price = 300, Category = "Супы", SortOrder = 20 };
            db.Dishes.AddRange(tea, soup);
            await db.SaveChangesAsync();
            var variant = new Dish { Name = "Стандарт", Price = 100, Category = "Напитки", ParentId = tea.Id };
            db.Dishes.Add(variant);
            await db.SaveChangesAsync();

            var controller = new DishesController(db);
            await controller.Reorder(new List<ReorderItem>
            {
                new() { Id = soup.Id, SortOrder = 10, Category = "Супы" },
                new() { Id = tea.Id, SortOrder = 20, Category = " Горячие напитки " },
            });

            Assert.Equal(10, soup.SortOrder);
            Assert.Equal(20, tea.SortOrder);
            Assert.Equal("Горячие напитки", tea.Category);
            Assert.Equal("Горячие напитки", variant.Category);
        }

        [Fact]
        public async Task Reorder_WithoutCategory_KeepsSection()
        {
            using var db = NewContext();
            var tea = new Dish { Name = "Чай", Price = 100, Category = "Напитки", SortOrder = 10 };
            db.Dishes.Add(tea);
            await db.SaveChangesAsync();

            await new DishesController(db).Reorder(new List<ReorderItem> { new() { Id = tea.Id, SortOrder = 50 } });

            Assert.Equal(50, tea.SortOrder);
            Assert.Equal("Напитки", tea.Category);
        }
    }
}
