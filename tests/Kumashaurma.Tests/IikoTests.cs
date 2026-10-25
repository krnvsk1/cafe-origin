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
        public void HashesPasswordWithSha1() =>
            Assert.Equal("da39a3ee5e6b4b0d3255bfef95601890afd80709", IikoClient.Sha1(""));

        [Fact]
        public void ParsesProductsAndCharts()
        {
            var products = IikoClient.ParseProducts(@"[
              {""id"":""d1"",""name"":""Шаурма классическая"",""type"":""DISH"",""mainUnit"":""u-portion"",""deleted"":false},
              {""id"":""g1"",""name"":""Лаваш"",""type"":""GOODS"",""mainUnit"":""u-pcs""},
              {""id"":""x"",""name"":""Удалённый"",""type"":""GOODS"",""deleted"":true}]");
            Assert.Equal(2, products.Count);
            Assert.Equal("DISH", products[0].Type);

            var charts = IikoClient.ParseCharts(@"{""assemblyCharts"":[
              {""assembledProductId"":""d1"",""dateFrom"":""2024-01-01"",""dateTo"":null,""assembledAmount"":1,
               ""items"":[{""productId"":""g1"",""amountIn"":1,""amountMiddle"":1,""amountOut"":1,""sortWeight"":0}]},
              {""assembledProductId"":""d1"",""dateFrom"":""2020-01-01"",""dateTo"":""2024-01-01"",""assembledAmount"":1,""items"":[]}
            ],""preparedCharts"":[]}");
            Assert.Equal(2, charts.Count);
            var active = IikoClient.ActiveCharts(charts, new DateTime(2026, 10, 25));
            Assert.Single(active);
            Assert.Single(active["d1"].Items);
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
