using Kumashaurma.API.Services;
using Xunit;
using Xunit.Abstractions;

namespace Kumashaurma.Tests
{
    public class MenuImportParserTests
    {
        private readonly ITestOutputHelper _output;
        public MenuImportParserTests(ITestOutputHelper output) => _output = output;

        private const string SampleHtml = """
            <div class="sect nav__anchor" data-id="43" data-pid="0" data-modifiers='[]' data-restriction=''>
              <h2 class="sect__title">Завтраки</h2>
              <div class="sect__dishes">
                <div class="ddish ddish--with-photo" data-id="416" data-modifiers='[{"title":"Выберите размер","type":"sizes","required":1,"options":[{"name":"с мясом 2 шт","price":2000000},{"name":"с творогом 2 шт","price":2500000}]},{"title":"добавки завтрак ","type":"many_many","required":false,"min":0,"max":0,"options":[{"id":411,"name":"Сметана","price":500000},{"id":1,"name":"Компот 0,33","price":1500000}]}]' data-prices='[]' data-media='[{"type":"photo","src":"https:\/\/cdn.example\/a_420.webp","high":"https:\/\/cdn.example\/a_1100.webp"}]'>
                  <div class="ddish__badges"><span class="ddish__badge ddish__badge--new"></span></div>
                  <div class="ddish__name"><a href="/product/416-bliny" class="ddish__link">Блины</a></div>
                  <div class="ddish__ingredients">Тесто , мясо,творог</div>
                </div>
                <div class="ddish" data-id="20" data-modifiers='[{"title":"Добавки в Пиццу","type":"one_one","required":true,"min":1,"max":0,"options":[{"name":"Тонкое тесто","price":0}]}]' data-media='[]'>
                  <div class="ddish__name"><a href="/product/20-p" class="ddish__link">Пицца «Маргарита»</a></div>
                </div>
              </div>
            </div>
            <div class="sect nav__anchor" data-id="12" data-pid="0">
              <h2 class="sect__title">Напитки</h2>
              <div class="ddish" data-id="5"><a href="/product/5-m" class="ddish__link">Морс</a></div>
            </div>
            """;

        [Fact]
        public void Parses_sections_dishes_sizes_and_addons()
        {
            var sections = MenuImportService.Parse(SampleHtml);

            Assert.Equal(2, sections.Count);
            Assert.Equal("Завтраки", sections[0].Title);
            Assert.Equal(2, sections[0].Dishes.Count);

            var bliny = sections[0].Dishes[0];
            Assert.Equal("Блины", bliny.Name);
            Assert.Equal("Тесто, мясо, творог", bliny.Description);
            Assert.Equal("new", bliny.Badge);
            Assert.Equal(new[] { 200m, 250m }, bliny.Sizes.Select(s => s.Price));
            Assert.Equal("с мясом 2 шт", bliny.Sizes[0].Name);
            Assert.Single(bliny.AddonGroups);
            Assert.Equal("добавки завтрак", bliny.AddonGroups[0].Title);
            Assert.Equal("Компот 0,33", bliny.AddonGroups[0].Options[1].Name);
            Assert.Equal(50m, bliny.AddonGroups[0].Options[0].Price);
            Assert.Equal("https://cdn.example/a_1100.webp", bliny.ImageUrl);

            var pizza = sections[0].Dishes[1];
            Assert.True(pizza.AddonGroups[0].SingleChoice);
            Assert.True(pizza.AddonGroups[0].Required);
            Assert.Null(pizza.ImageUrl);

            Assert.Equal("Морс", sections[1].Dishes[0].Name);
            Assert.Empty(sections[1].Dishes[0].Sizes);
        }

        // Проверка на живом сайте: запускается, только если задана переменная KUMASHAURMA_LIVE_TEST=1
        [Fact]
        public async Task Parses_live_site_when_enabled()
        {
            if (Environment.GetEnvironmentVariable("KUMASHAURMA_LIVE_TEST") != "1") return;

            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 KumashaurmaImport");
            var html = await http.GetStringAsync(MenuImportService.DefaultSourceUrl);
            var sections = MenuImportService.Parse(html);

            foreach (var s in sections)
                _output.WriteLine($"{s.Title}: {s.Dishes.Count} — {string.Join("; ", s.Dishes.Take(3).Select(d => $"{d.Name} [{string.Join(", ", d.Sizes.Select(x => $"{x.Name}={x.Price}"))}] +{d.AddonGroups.Sum(g => g.Options.Count)} доб."))}");

            Assert.True(sections.Count >= 10, $"Разделов: {sections.Count}");
            var dishes = sections.SelectMany(s => s.Dishes).ToList();
            Assert.True(dishes.Count >= 80, $"Блюд: {dishes.Count}");
            Assert.True(dishes.Count(d => d.ImageUrl != null) >= dishes.Count * 0.8);
            Assert.True(dishes.All(d => d.Sizes.All(x => x.Price >= 0)));
        }
    }
}
