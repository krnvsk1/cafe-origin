using System.Text.Json;
using Cafe.API.Models;
using Cafe.API.Services;
using Xunit;

namespace Cafe.Tests
{
    public class KitchenTechCardsTests
    {
        private static (TechCardCalculator Calc, TechCard Dish) Setup()
        {
            var chicken = new StockItem { Id = 1, Name = "Курица", Unit = StockUnits.Kg, Kind = StockItemKinds.Raw, AvgCost = 300 };
            var sauce = new StockItem { Id = 2, Name = "Соус чесночный", Unit = StockUnits.Kg, Kind = StockItemKinds.Semi, AvgCost = 200 };
            var garlic = new StockItem { Id = 3, Name = "Чеснок", Unit = StockUnits.Kg, Kind = StockItemKinds.Raw, AvgCost = 400 };

            var dish = new TechCard { Id = 10, DishId = 100, OutputWeight = 350, Instructions = "  Обжарить курицу, завернуть  " };
            dish.Lines.Add(new TechCardLine { StockItemId = 1, Gross = 0.2m, LossPercent = 25, SortOrder = 0 });
            dish.Lines.Add(new TechCardLine { StockItemId = 2, Gross = 0.03m, SortOrder = 1 });

            var sauceCard = new TechCard { Id = 11, StockItemId = 2, YieldQuantity = 1 };
            sauceCard.Lines.Add(new TechCardLine { StockItemId = 3, Gross = 0.05m });

            return (new TechCardCalculator(new[] { chicken, sauce, garlic }, new[] { dish, sauceCard }), dish);
        }

        [Fact]
        public void View_HasCompositionAndTechnology_ButNoCosts()
        {
            var (calc, dish) = Setup();

            var view = KitchenTechCards.ToView(dish, "Бургер · Большая", calc)!;

            Assert.Equal(350, view.OutputWeight);
            Assert.Equal("Обжарить курицу, завернуть", view.Instructions);
            Assert.Equal(new[] { "Курица", "Соус чесночный" }, view.Lines.Select(l => l.Name));
            Assert.Equal(0.15m, view.Lines[0].Net);   // 0.2 − 25 %
            Assert.True(view.Lines[1].IsSemi);
            // Повар не должен видеть себестоимость
            var json = JsonSerializer.Serialize(view).ToLowerInvariant();
            Assert.DoesNotContain("cost", json);
            Assert.DoesNotContain("price", json);
        }

        [Fact]
        public void VariantWithoutOwnCard_UsesParentCard()
        {
            var (calc, _) = Setup();
            Assert.Equal(10, calc.ForDish(dishId: 101, parentId: 100)!.Id);
            Assert.Null(KitchenTechCards.ToView(calc.ForDish(dishId: 101), "Без карты", calc));
        }

        [Fact]
        public void SemiCards_IncludeRecipesOfSemiFinishedIngredients_Once()
        {
            var (calc, dish) = Setup();
            var view = KitchenTechCards.ToView(dish, "Бургер", calc)!;

            var semis = KitchenTechCards.SemiCards(new[] { view, view }, calc);

            var sauce = Assert.Single(semis);
            Assert.Equal("Соус чесночный", sauce.Title);
            Assert.Equal("Чеснок", Assert.Single(sauce.Lines).Name);
        }
    }
}
