using Kumashaurma.API.Services.Iiko;
using Xunit;

namespace Kumashaurma.Tests
{
    public class IikoTests
    {
        [Theory]
        [InlineData("192.168.1.10:8080", "http://192.168.1.10:8080/resto")]
        [InlineData("http://192.168.1.10:8080/resto/", "http://192.168.1.10:8080/resto")]
        [InlineData("https://shop.iiko.it:443/resto/api/auth", "https://shop.iiko.it:443/resto")]
        public void NormalizesBaseUrl(string input, string expected) => Assert.Equal(expected, IikoClient.NormalizeBaseUrl(input));

        [Fact]
        public void ParsesIikoCsvExports()
        {
            var goods = IikoClient.ParseGoodsCsv(
                "TYPE;NUM;PARENT_CODE;NAME;MEASURE_UNIT;UNIT_WEIGHT;PRICE;\n" +
                "DISH;02415;0251;Хачапури \"Нарта\" 33 см;порц;0,800;650,00;\n" +
                "GOODS;00686;0010;Лаваш;шт;0,000;0,00;\r\n" +
                "PREPARED;02548;0060;П/Ф СОУС ГОРЧИЧНЫЙ;кг;1,000;0,00;\n");
            Assert.Equal(3, goods.Count);
            Assert.Equal("02415", goods[0].Id);
            Assert.Equal("Хачапури \"Нарта\" 33 см", goods[0].Name);
            Assert.Equal("шт", goods[1].Unit);
            Assert.Equal("PREPARED", goods[2].Type);

            var charts = IikoClient.ParseChartsCsv(
                "PRODUCT_CODE;ITEM_CODE;ITEM_GROSS_WEIGHT;ITEM_NET_WEIGHT;ITEM_OUT_WEIGHT;TECHNOLOGY;AMOUNT;DATE_FROM;WRITEOFF_TYPE;\n" +
                "02415;00686;1.000000000;0.089000000;0.089000000;;1.000000000;16.05.2026;1;\n" +
                "02415;02548;0.050000000;0.050000000;0.045000000;Смешать;1.000000000;16.05.2026;1;\n" +
                "02415;00686;2.000000000;0.178000000;0.178000000;;1.000000000;16.05.2024;1;\n" +
                "02548;00025;8.000000000;8.000000000;7.500000000;;7.500000000;16.03.2024;1;\n");
            var active = IikoClient.ActiveCharts(charts, new DateTime(2026, 10, 25));
            Assert.Equal(2, active.Count);
            Assert.Equal(2, active["02415"].Items.Count);
            Assert.Equal("Смешать", active["02415"].Technology);
            Assert.Equal(0.045m, active["02415"].Items[1].AmountOut);
            Assert.Equal(7.5m, active["02548"].AssembledAmount);

            // Раньше 16.05.2026 действовала старая версия карты
            var old = IikoClient.ActiveCharts(charts, new DateTime(2025, 1, 1));
            Assert.Equal(2m, Assert.Single(old["02415"].Items).AmountIn);
        }

        [Fact]
        public void CountsPieceAndSemiIngredientsInOutputWeight()
        {
            var s = new IikoSnapshot();
            s.Products["lav"] = new IikoProduct { Id = "lav", Name = "Лаваш", Type = "GOODS", Unit = "шт" };
            s.Products["cut"] = new IikoProduct { Id = "cut", Name = "П/Ф котлета", Type = "PREPARED", Unit = "шт", UnitWeight = 0.147m };
            s.Products["sauce"] = new IikoProduct { Id = "sauce", Name = "П/Ф соус", Type = "PREPARED", Unit = "кг" };
            var chart = new IikoChart
            {
                AssembledAmount = 1,
                Items =
                {
                    new IikoChartItem { ProductId = "lav", AmountIn = 1, AmountMiddle = 0.089m, AmountOut = 0.089m },  // 1 шт = 89 г
                    new IikoChartItem { ProductId = "cut", AmountIn = 1 },                                          // 1 шт × 147 г
                    new IikoChartItem { ProductId = "sauce", AmountIn = 0.05m, AmountMiddle = 0.05m, AmountOut = 0.045m },
                }
            };
            var dish = new IikoProduct { Id = "d", Name = "Шаурма", Type = "DISH", Unit = "кг" };
            Assert.Equal(281m, IikoImportService.OutputGrams(dish, chart, s));

            // Если у блюда в порциях указан вес порции — берём его
            var portion = new IikoProduct { Id = "d2", Name = "Шаурма", Type = "DISH", Unit = "порц", UnitWeight = 0.3m };
            Assert.Equal(300m, IikoImportService.OutputGrams(portion, chart, s));
        }

        [Theory]
        [InlineData("кг", "kg", 1)]
        [InlineData("г", "kg", 0.001)]
        [InlineData("мл", "l", 0.001)]
        [InlineData("порц", "pcs", 1)]
        public void MapsUnits(string unit, string stock, decimal factor)
        {
            var (u, f) = IikoImportService.UnitOf(unit);
            Assert.Equal(stock, u);
            Assert.Equal(factor, f);
        }

        [Fact]
        public void NormalizesNames() =>
            Assert.Equal("шаурма с курицей 30 см", IikoImportService.Normalize("Шаурма  с курицей, 30 см!"));
    }
}
