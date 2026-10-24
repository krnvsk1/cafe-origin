using System.Xml.Linq;
using Kumashaurma.API.Models;
using Kumashaurma.API.Services.Egais;
using Xunit;

namespace Kumashaurma.Tests
{
    public class EgaisTests
    {
        private const string WayBill = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<ns:Documents Version=""1.0"" xmlns:ns=""http://fsrar.ru/WEGAIS/WB_DOC_SINGLE_01""
  xmlns:wb=""http://fsrar.ru/WEGAIS/TTNSingle_v4"" xmlns:ce=""http://fsrar.ru/WEGAIS/CommonV3""
  xmlns:oref=""http://fsrar.ru/WEGAIS/ClientRef_v2"" xmlns:pref=""http://fsrar.ru/WEGAIS/ProductRef_v2"">
  <ns:Owner><ns:FSRAR_ID>030000000001</ns:FSRAR_ID></ns:Owner>
  <ns:Document>
    <ns:WayBill_v4>
      <wb:Identity>WB-777</wb:Identity>
      <wb:Header>
        <wb:NUMBER>ПН-1024</wb:NUMBER>
        <wb:Date>2026-10-20</wb:Date>
        <wb:Shipper><oref:UL><oref:INN>7700000000</oref:INN><oref:ClientRegId>010000000002</oref:ClientRegId><oref:FullName>ООО «Пивной двор»</oref:FullName><oref:ShortName>Пивной двор</oref:ShortName></oref:UL></wb:Shipper>
        <wb:Consignee><oref:UL><oref:ClientRegId>030000000001</oref:ClientRegId></oref:UL></wb:Consignee>
      </wb:Header>
      <wb:Content>
        <wb:Position>
          <wb:Identity>1</wb:Identity>
          <wb:Product><pref:UnitType>Packed</pref:UnitType><pref:FullName>Пиво светлое 0,5</pref:FullName><pref:AlcCode>0300000000001111111</pref:AlcCode><pref:Capacity>0.5000</pref:Capacity><pref:AlcVolume>4.700</pref:AlcVolume></wb:Product>
          <wb:Quantity>24</wb:Quantity>
          <wb:Price>65.50</wb:Price>
          <wb:InformF2><ce:F2RegId>FB-000000001</ce:F2RegId></wb:InformF2>
        </wb:Position>
        <wb:Position>
          <wb:Identity>2</wb:Identity>
          <wb:Product><pref:UnitType>Unpacked</pref:UnitType><pref:FullName>Пиво разливное нефильтрованное</pref:FullName><pref:AlcCode>0300000000002222222</pref:AlcCode><pref:AlcVolume>5.000</pref:AlcVolume></wb:Product>
          <wb:Quantity>3</wb:Quantity>
          <wb:Price>1200</wb:Price>
        </wb:Position>
      </wb:Content>
    </ns:WayBill_v4>
  </ns:Document>
</ns:Documents>";

        private const string RegInfo = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<ns:Documents Version=""1.0"" xmlns:ns=""http://fsrar.ru/WEGAIS/WB_DOC_SINGLE_01"" xmlns:wbr=""http://fsrar.ru/WEGAIS/TTNInformF2Reg"">
  <ns:Owner><ns:FSRAR_ID>030000000001</ns:FSRAR_ID></ns:Owner>
  <ns:Document>
    <ns:TTNInformF2Reg>
      <wbr:Header>
        <wbr:Identity>WB-777</wbr:Identity>
        <wbr:WBRegId>TTN-0123456789</wbr:WBRegId>
        <wbr:WBNUMBER>ПН-1024</wbr:WBNUMBER>
        <wbr:WBDate>2026-10-20</wbr:WBDate>
      </wbr:Header>
      <wbr:Content>
        <wbr:Position><wbr:Identity>1</wbr:Identity><wbr:InformF2RegId>FB-000000101</wbr:InformF2RegId></wbr:Position>
        <wbr:Position><wbr:Identity>2</wbr:Identity><wbr:InformF2RegId>FB-000000102</wbr:InformF2RegId></wbr:Position>
      </wbr:Content>
    </ns:TTNInformF2Reg>
  </ns:Document>
</ns:Documents>";

        private const string Ticket = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<ns:Documents Version=""1.0"" xmlns:ns=""http://fsrar.ru/WEGAIS/WB_DOC_SINGLE_01"" xmlns:tc=""http://fsrar.ru/WEGAIS/Ticket"">
  <ns:Owner><ns:FSRAR_ID>030000000001</ns:FSRAR_ID></ns:Owner>
  <ns:Document>
    <ns:Ticket>
      <tc:TicketDate>2026-10-21T10:00:00</tc:TicketDate>
      <tc:DocType>WayBillAct_v4</tc:DocType>
      <tc:RegID>TTN-0123456789</tc:RegID>
      <tc:OperationResult>
        <tc:OperationName>Confirm</tc:OperationName>
        <tc:OperationResult>Accepted</tc:OperationResult>
        <tc:OperationComment>Накладная подтверждена</tc:OperationComment>
      </tc:OperationResult>
    </ns:Ticket>
  </ns:Document>
</ns:Documents>";

        [Fact]
        public void ParsesWayBill()
        {
            var wb = EgaisXml.ParseWayBill(EgaisXml.Load(WayBill));
            Assert.Equal("WB-777", wb.Identity);
            Assert.Equal("ПН-1024", wb.Number);
            Assert.Equal("010000000002", wb.ShipperFsrarId);
            Assert.Equal("7700000000", wb.ShipperInn);
            Assert.Equal("Пивной двор", wb.ShipperName);
            Assert.Equal(2, wb.Positions.Count);

            var bottle = wb.Positions[0];
            Assert.Equal("Packed", bottle.UnitType);
            Assert.Equal(0.5m, bottle.Capacity);
            Assert.Equal(24m, bottle.Quantity);
            Assert.Equal(65.5m, bottle.Price);
            Assert.Equal("FB-000000001", bottle.F2RegId);
            Assert.Equal("Unpacked", wb.Positions[1].UnitType);
        }

        [Fact]
        public void ParsesRegInfoAndTicket()
        {
            Assert.Equal("TTN-0123456789", EgaisXml.ParseRegInfo(EgaisXml.Load(RegInfo)).WbRegId);
            Assert.Equal("FB-000000102", EgaisXml.ParseRegInfo(EgaisXml.Load(RegInfo)).F2RegIdByPosition["2"]);
            Assert.Equal("TTNInformF2Reg", EgaisXml.DetectType(EgaisXml.Load(RegInfo)));

            var ticket = EgaisXml.ParseTicket(EgaisXml.Load(Ticket));
            Assert.Equal("TTN-0123456789", ticket.RegId);
            Assert.True(ticket.Accepted);
            Assert.Equal("Накладная подтверждена", ticket.Comment);
        }

        [Fact]
        public void BuildsActWithDifferences()
        {
            var xml = EgaisXml.BuildWayBillAct("030000000001", ActDecision.Differences, "A1", new DateTime(2026, 10, 21),
                "TTN-0123456789", "Бой 2 бутылки", new[] { new ActPosition("1", "FB-000000101", 22m) });

            var doc = XDocument.Parse(xml);
            XNamespace wa = EgaisXml.NsActV4, ns = EgaisXml.NsDocuments;
            Assert.Equal("030000000001", doc.Root!.Element(ns + "Owner")!.Element(ns + "FSRAR_ID")!.Value);
            var act = doc.Root.Element(ns + "Document")!.Element(ns + "WayBillAct_v4")!;
            var header = act.Element(wa + "Header")!;
            Assert.Equal("Differences", header.Element(wa + "IsAccept")!.Value);
            Assert.Equal("2026-10-21", header.Element(wa + "ActDate")!.Value);
            Assert.Equal("TTN-0123456789", header.Element(wa + "WBRegId")!.Value);
            var position = act.Element(wa + "Content")!.Element(wa + "Position")!;
            Assert.Equal("22", position.Element(wa + "RealQuantity")!.Value);
            Assert.Equal("FB-000000101", position.Element(wa + "InformF2RegId")!.Value);
        }

        [Fact]
        public void AcceptActHasEmptyContent()
        {
            var xml = EgaisXml.BuildWayBillAct("030000000001", ActDecision.Accepted, "A2", DateTime.Today, "TTN-1", null, null);
            Assert.Contains("<wa:IsAccept>Accepted</wa:IsAccept>", xml);
            Assert.DoesNotContain("Position", xml);
        }

        [Fact]
        public void ParsesUtmOutList()
        {
            var list = UtmClient.ParseList(@"<?xml version=""1.0"" encoding=""UTF-8""?><A>
  <url replyId=""abc"">http://localhost:8080/opt/out/WayBill_v4/12</url>
  <url>http://localhost:8080/opt/out/FORM2REGINFO/13</url>
  <ver>2</ver></A>");
            Assert.Equal(2, list.Count);
            Assert.Equal("WayBill_v4", list[0].Type);
            Assert.Equal("abc", list[0].ReplyId);
            Assert.Equal("FORM2REGINFO", list[1].Type);
        }

        [Fact]
        public void ConvertsEgaisQuantityToStockUnits()
        {
            var liters = new StockItem { Name = "Пиво", Unit = StockUnits.Liter };
            var pieces = new StockItem { Name = "Пиво 0,5", Unit = StockUnits.Piece };
            var keg = new EgaisDocumentLine { ProductName = "Кега", UnitType = "Unpacked" };
            var bottle = new EgaisDocumentLine { ProductName = "Бутылка", UnitType = "Packed", Capacity = 0.5m };

            Assert.Equal(30m, EgaisService.ConvertToStockUnits(keg, liters, 3m));      // 3 дал = 30 л
            Assert.Equal(12m, EgaisService.ConvertToStockUnits(bottle, liters, 24m));   // 24 × 0,5 л
            Assert.Equal(24m, EgaisService.ConvertToStockUnits(bottle, pieces, 24m));
            Assert.Throws<EgaisException>(() => EgaisService.ConvertToStockUnits(keg, pieces, 1m));
        }

        [Fact]
        public void SalesChannels_RestrictByCardAndVariant()
        {
            Assert.True(SalesChannels.Allows(null, null, SalesChannels.Delivery));
            Assert.False(SalesChannels.Allows(null, "В зале", SalesChannels.Delivery));
            Assert.True(SalesChannels.Allows(null, "В зале", SalesChannels.DineIn));
            Assert.Null(SalesChannels.Format(new[] { "Доставка", "Самовывоз", "В зале" }));
            Assert.Equal("Самовывоз,В зале", SalesChannels.Format(new[] { "В зале", "Самовывоз" }));
        }
    
        [Fact]
        public void SettingsValidation()
        {
            Assert.Null(EgaisSettingsProvider.Validate(new EgaisOptions { Enabled = true, UtmUrl = "http://192.168.1.10:8080", FsrarId = "030000123456", AutoSyncMinutes = 10 }));
            Assert.NotNull(EgaisSettingsProvider.Validate(new EgaisOptions { Enabled = true, UtmUrl = "http://localhost:8080", FsrarId = "123" }));
            Assert.NotNull(EgaisSettingsProvider.Validate(new EgaisOptions { UtmUrl = "ftp://host" }));
            // Выключенный ЕГАИС можно сохранить без ФСРАР ID
            Assert.Null(EgaisSettingsProvider.Validate(new EgaisOptions { Enabled = false, UtmUrl = "http://localhost:8080" }));
        }
    }
}
