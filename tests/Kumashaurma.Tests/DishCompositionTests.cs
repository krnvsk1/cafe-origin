using Kumashaurma.API.Models;
using Kumashaurma.API.Services;
using Xunit;

namespace Kumashaurma.Tests
{
    /// <summary>Товары → полуфабрикаты → блюда → блюда в блюдах (сет, «½ пиццы»)</summary>
    public class DishCompositionTests
    {
        private static TechCardCalculator Setup(out TechCard combo)
        {
            var flour = new StockItem { Id = 1, Name = "Мука", Unit = StockUnits.Kg, Kind = StockItemKinds.Raw, AvgCost = 50 };
            var cheese = new StockItem { Id = 2, Name = "Сыр", Unit = StockUnits.Kg, Kind = StockItemKinds.Raw, AvgCost = 600 };
            var dough = new StockItem { Id = 3, Name = "П/Ф тесто", Unit = StockUnits.Kg, Kind = StockItemKinds.Semi };
            var cola = new StockItem { Id = 4, Name = "Кола 0,5", Unit = StockUnits.Piece, Kind = StockItemKinds.Raw, AvgCost = 60 };

            // Тесто: 1 кг муки → 1,6 кг теста
            var doughCard = new TechCard { Id = 10, StockItemId = 3, YieldQuantity = 1.6m };
            doughCard.Lines.Add(new TechCardLine { StockItemId = 1, Gross = 1m });

            // Пицца: 0,4 кг теста + 0,2 кг сыра
            var pizza = new TechCard { Id = 20, ShawarmaId = 100 };
            pizza.Lines.Add(new TechCardLine { StockItemId = 3, Gross = 0.4m });
            pizza.Lines.Add(new TechCardLine { StockItemId = 2, Gross = 0.2m });

            // Комбо: ½ пиццы + кола
            combo = new TechCard { Id = 30, ShawarmaId = 200 };
            combo.Lines.Add(new TechCardLine { SubDishId = 100, Gross = 0.5m });
            combo.Lines.Add(new TechCardLine { StockItemId = 4, Gross = 1m });

            return new TechCardCalculator(new[] { flour, cheese, dough, cola }, new[] { doughCard, pizza, combo },
                new[] { (100, (int?)null, "Пицца"), (200, (int?)null, "Комбо"), (201, (int?)200, "Комбо · большое") });
        }

        [Fact]
        public void ComboExpandsHalfPizzaDownToGoods()
        {
            var calc = Setup(out var combo);
            var raw = calc.ExpandToRaw(combo, portions: 2);
            Assert.Equal(0.25m, raw[1]);   // мука: 2 × ½ × 0,4 кг теста / 1,6
            Assert.Equal(0.2m, raw[2]);    // сыр: 2 × ½ × 0,2
            Assert.Equal(2m, raw[4]);      // кола
            Assert.False(raw.ContainsKey(3)); // полуфабрикат раскрыт до товаров
        }

        [Fact]
        public void CostIncludesShareOfNestedDish()
        {
            var calc = Setup(out var combo);
            // Пицца: 0,4/1,6 × 50 + 0,2 × 600 = 12,5 + 120 = 132,5; половина = 66,25; + кола 60
            Assert.Equal(132.5m, calc.DishCost(100));
            Assert.Equal(126.25m, calc.CardCost(combo));
        }

        [Fact]
        public void DetectsDishCycles()
        {
            var calc = Setup(out _);
            // Пицца не может включать комбо — комбо уже содержит пиццу
            Assert.True(calc.CreatesDishCycle(100, new[] { 200 }));
            // Вариант комбо без своей карты списывается по карте комбо — комбо не может включать свой вариант
            Assert.True(calc.CreatesDishCycle(200, new[] { 201 }));
            Assert.False(calc.CreatesDishCycle(200, new[] { 100 }));
        }

        [Fact]
        public void KitchenShowsNestedDishAsPortionAndItsCard()
        {
            var calc = Setup(out var combo);
            var view = KitchenTechCards.ToView(combo, "Комбо", calc)!;
            var half = view.Lines[0];
            Assert.True(half.IsDish);
            Assert.Equal("portion", half.Unit);
            Assert.Equal(0.5m, half.Gross);
            Assert.Equal("Пицца", half.Name);
            var nested = KitchenTechCards.SemiCards(new[] { view }, calc);
            Assert.Contains(nested, c => c.Title == "Пицца");
        }
    }
}
