using System.Text;
using System.Text.Json.Nodes;
using Cafe.API.Services.Edo;
using Xunit;

namespace Cafe.Tests
{
    public class EdoTests
    {
        private const string Upd501 = @"<?xml version=""1.0"" encoding=""windows-1251""?>
<Файл ИдФайл=""ON_NSCHFDOPPR_2BM-615015820463_2BE-7700000000_20261020_abc"" ВерсФорм=""5.01"" ВерсПрог=""Saby"">
  <Документ КНД=""1115131"" Функция=""СЧФДОП"" ДатаИнфПр=""20.10.2026"">
    <СвСчФакт НомерСчФ=""УТ-1234"" ДатаСчФ=""20.10.2026"" КодОКВ=""643"">
      <СвПрод><ИдСв><СвЮЛУч НаимОрг=""ООО &quot;Молочник&quot;"" ИННЮЛ=""7700000000"" КПП=""770001001""/></ИдСв></СвПрод>
      <СвПокуп><ИдСв><СвИП ИННФЛ=""615015820463""><ФИО Фамилия=""Шевченко"" Имя=""А"" Отчество=""Д""/></СвИП></ИдСв></СвПокуп>
    </СвСчФакт>
    <ТаблСчФакт>
      <СведТов НомСтр=""1"" НаимТов=""Молоко 3,2% 1 л"" ОКЕИ_Тов=""796"" КолТов=""12"" ЦенаТов=""80.00"" СтТовБезНДС=""960.00"" НалСт=""10%"" СтТовУчНал=""1056.00"">
        <ДопСведТов КодТов=""M-001"" НаимЕдИзм=""шт"">
          <НомСредИдентТов><КИЗ>0104607009471233215(abcd</КИЗ><КИЗ>010460700947123321XyZ123</КИЗ></НомСредИдентТов>
        </ДопСведТов>
      </СведТов>
      <СведТов НомСтр=""2"" НаимТов=""Сыр Моцарелла"" ОКЕИ_Тов=""166"" КолТов=""2,5"" ЦенаТов=""600"" СтТовБезНДС=""1500"" НалСт=""без НДС"" СтТовУчНал=""1500""/>
      <ВсегоОпл СтТовБезНДСВсего=""2460"" СтТовУчНалВсего=""2556.00""/>
    </ТаблСчФакт>
  </Документ>
</Файл>";

        private const string Upd503 = @"<Файл ИдФайл=""ON_NSCHFDOPPR_x"" ВерсФорм=""5.03"">
  <Документ>
    <СвСчФакт НомерДок=""77"" ДатаДок=""01.10.2026"">
      <СвПрод><ИдСв><СвИП ИННФЛ=""500100732259""><ФИО Фамилия=""Иванов"" Имя=""Иван"" Отчество=""Иванович""/></СвИП></ИдСв></СвПрод>
    </СвСчФакт>
    <ТаблСчФакт>
      <СведТов НомСтр=""1"" НаимТов=""Лаваш"" ОКЕИ_Тов=""796"" НаимЕдИзм=""шт"" КолТов=""100"" ЦенаТов=""10"" СтТовБезНДС=""1000"" НалСт=""без НДС"" СтТовУчНал=""1000""/>
    </ТаблСчФакт>
  </Документ>
</Файл>";

        [Fact]
        public void ParsesUpd501()
        {
            var u = UpdParser.Parse(Upd501);
            Assert.Equal("УТ-1234", u.Number);
            Assert.Equal(new DateTime(2026, 10, 20), u.Date!.Value.Date);
            Assert.Equal("7700000000", u.SellerInn);
            Assert.Equal("770001001", u.SellerKpp);
            Assert.Equal("ООО \"Молочник\"", u.SellerName);
            Assert.Equal("615015820463", u.BuyerInn);
            Assert.Equal(2556m, u.Total);
            Assert.Equal(2, u.Lines.Count);

            var milk = u.Lines[0];
            Assert.Equal(12m, milk.Quantity);
            Assert.Equal(1056m, milk.Sum);
            Assert.Equal("M-001", milk.Code);
            Assert.Equal("шт", milk.UnitName);
            Assert.Equal(2, milk.Codes.Count);
            Assert.Equal("0104607009471233215(abcd", milk.Codes[0]);
            Assert.Equal("04607009471233", milk.Gtin);
            Assert.Equal("gtin:04607009471233", milk.Key);

            var cheese = u.Lines[1];
            Assert.Equal(2.5m, cheese.Quantity);
            Assert.Equal("166", cheese.Okei);
            Assert.Equal("name:сыр моцарелла", cheese.Key);
        }

        [Fact]
        public void ParsesUpd503FromIp()
        {
            var u = UpdParser.Parse(Upd503);
            Assert.Equal("77", u.Number);
            Assert.Equal("500100732259", u.SellerInn);
            Assert.Equal("ИП Иванов Иван Иванович", u.SellerName);
            Assert.Equal("шт", u.Lines[0].UnitName);
            Assert.Equal(1000m, u.Total);
        }

        [Fact]
        public void DecodesWindows1251File()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var bytes = Encoding.GetEncoding(1251).GetBytes(Upd501);
            var xml = UpdParser.DecodeFile(bytes);
            Assert.Equal("Молоко 3,2% 1 л", UpdParser.Parse(xml).Lines[0].Name);
        }

        [Fact]
        public void RejectsNonUpd()
        {
            Assert.Throws<UpdParseException>(() => UpdParser.Parse("<Файл><Что-то/></Файл>"));
            Assert.True(UpdParser.IsUpdFileName("ON_NSCHFDOPPR_2BM_x.xml"));
            Assert.False(UpdParser.IsUpdFileName("DP_IZVPOL_x.xml"));
        }

        [Fact]
        public void DefaultFactorByOkei()
        {
            Assert.Equal(0.001m, UpdParser.DefaultFactor("163", "kg"));
            Assert.Equal(1m, UpdParser.DefaultFactor("166", "kg"));
            Assert.Equal(1m, UpdParser.DefaultFactor("796", "pcs"));
        }

        [Fact]
        public void ParsesChangesList()
        {
            var json = JsonNode.Parse(@"{
              ""Документ"": [{
                ""Идентификатор"": ""doc-1"", ""Направление"": ""Входящий"", ""Удален"": ""Нет"", ""Номер"": ""12"", ""Дата"": ""20.10.2026"",
                ""Контрагент"": { ""СвЮЛ"": { ""ИНН"": ""7700000000"", ""Название"": ""ООО Молочник"" } },
                ""Событие"": [{
                  ""Идентификатор"": ""ev-1"", ""ДатаВремя"": ""20.10.2026 10.15.30"", ""Название"": ""Получение"",
                  ""Вложение"": [{ ""Название"": ""УПД"", ""Служебный"": ""Нет"", ""Файл"": { ""Имя"": ""ON_NSCHFDOPPR_1.xml"", ""Ссылка"": ""https://x/1"" } }]
                }]
              }],
              ""Навигация"": { ""ЕстьЕще"": ""Да"" }
            }");
            var page = SbisClient.ParseChanges(json);
            Assert.True(page.HasMore);
            var d = Assert.Single(page.Documents);
            Assert.Equal("doc-1", d.Id);
            Assert.Equal("7700000000", d.CounterpartyInn);
            Assert.Equal("https://x/1", d.Events[0].Attachments[0].FileUrl);
            Assert.Equal(new DateTime(2026, 10, 20, 7, 15, 30, DateTimeKind.Utc), SbisClient.ParseDateTime(d.Events[0].DateTime));
        }
    }
}
