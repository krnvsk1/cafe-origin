using System.Xml.Linq;
using Kumashaurma.API.Services.Mercury;
using Xunit;
using static Kumashaurma.API.Services.Mercury.VetisClient;

namespace Kumashaurma.Tests
{
    public class MercuryTests
    {
        private static readonly MercuryOptions Options = new()
        {
            ApiKey = "key", Login = "api-user", Password = "pwd", IssuerId = "issuer", InitiatorLogin = "kuma_vet",
            EnterpriseGuid = "11111111-2222-3333-4444-555555555555"
        };

        private static XElement Response() => XElement.Parse($@"
<merc:getVetDocumentListResponse xmlns:merc=""{Merc}"" xmlns:vd=""{Vd}"" xmlns:dt=""{Dt}"" xmlns:bs=""{Bs}"">
  <vd:vetDocumentList total=""1"" offset=""0"" count=""1"">
    <vd:vetDocument>
      <bs:uuid>aaaa-1111</bs:uuid>
      <vd:issueDate>2026-10-27</vd:issueDate>
      <vd:vetDType>TRANSPORT</vd:vetDType>
      <vd:vetDStatus>CONFIRMED</vd:vetDStatus>
      <vd:certifiedConsignment>
        <vd:consignor>
          <dt:businessEntity><bs:guid>be-1</bs:guid><dt:name>ООО Курица</dt:name></dt:businessEntity>
          <dt:enterprise><bs:guid>ent-1</bs:guid></dt:enterprise>
        </vd:consignor>
        <vd:consignee>
          <dt:businessEntity><bs:guid>be-2</bs:guid></dt:businessEntity>
          <dt:enterprise><bs:guid>ent-2</bs:guid></dt:enterprise>
        </vd:consignee>
        <vd:transportInfo><vd:transportType>1</vd:transportType><vd:transportNumber><vd:vehicleNumber>А123ВС77</vd:vehicleNumber></vd:transportNumber></vd:transportInfo>
        <vd:transportStorageType>CHILLED</vd:transportStorageType>
        <vd:batch>
          <vd:productType>1</vd:productType>
          <vd:product><bs:guid>p</bs:guid></vd:product>
          <vd:subProduct><bs:guid>sp</bs:guid></vd:subProduct>
          <vd:productItem><bs:guid>pi</bs:guid><dt:name>Филе бедра цыплёнка охл.</dt:name></vd:productItem>
          <vd:volume>12.5</vd:volume>
          <vd:unit><bs:guid>u</bs:guid><dt:name>кг</dt:name></vd:unit>
          <vd:dateOfProduction><vd:firstDate><bs:year>2026</bs:year><bs:month>10</bs:month><bs:day>25</bs:day></vd:firstDate></vd:dateOfProduction>
          <vd:expiryDate><vd:firstDate><bs:year>2026</bs:year><bs:month>11</bs:month><bs:day>1</bs:day></vd:firstDate></vd:expiryDate>
          <vd:perishable>true</vd:perishable>
          <vd:origin><vd:country><bs:guid>ru</bs:guid></vd:country></vd:origin>
          <vd:lowGradeCargo>false</vd:lowGradeCargo>
        </vd:batch>
      </vd:certifiedConsignment>
      <vd:referencedDocument><vd:issueNumber>УТ-77</vd:issueNumber><vd:issueDate>2026-10-27</vd:issueDate><vd:type>1</vd:type></vd:referencedDocument>
    </vd:vetDocument>
  </vd:vetDocumentList>
</merc:getVetDocumentListResponse>");

        [Fact]
        public void ParsesIncomingVetDocument()
        {
            var (docs, total) = VetDocuments.ParseList(Response());
            Assert.Equal(1, total);
            var d = Assert.Single(docs);
            Assert.Equal("aaaa-1111", d.Uuid);
            Assert.Equal("Филе бедра цыплёнка охл.", d.ProductName);
            Assert.Equal(12.5m, d.Volume);
            Assert.Equal("кг", d.UnitName);
            Assert.Equal("25.10.2026", d.ProductionDate);
            Assert.Equal("01.11.2026", d.ExpiryDate);
            Assert.Equal("ООО Курица", d.ConsignorName);
            Assert.Equal("№ УТ-77 от 27.10.2026", d.Waybill);
        }

        [Fact]
        public void AcceptRequest_CopiesConsignmentAndAcceptsAll()
        {
            var doc = VetDocuments.ParseList(Response()).Docs[0].Element;
            var req = VetDocuments.AcceptRequest(Options, doc, new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc));
            Assert.Equal(Merc + "processIncomingConsignmentRequest", req.Name);
            var delivery = req.Element(Merc + "delivery")!;
            Assert.Equal("2026-10-28T12:00:00", delivery.Element(Vd + "deliveryDate")!.Value);
            Assert.Equal("12.5", delivery.Element(Vd + "consignment")!.Element(Vd + "volume")!.Value);
            Assert.Equal("ent-2", delivery.Element(Vd + "consignee")!.Element(Dt + "enterprise")!.Element(Bs + "guid")!.Value);
            Assert.Equal("А123ВС77", delivery.Element(Vd + "transportInfo")!.Descendants(Vd + "vehicleNumber").Single().Value);
            var forms = delivery.Element(Vd + "accompanyingForms")!;
            Assert.Equal("УТ-77", forms.Element(Vd + "waybill")!.Element(Vd + "issueNumber")!.Value);
            Assert.Equal("aaaa-1111", forms.Element(Vd + "vetCertificate")!.Element(Bs + "uuid")!.Value);
            var facts = req.Element(Merc + "deliveryFacts")!;
            Assert.Equal("ACCEPT_ALL", facts.Element(Vd + "decision")!.Value);
            Assert.Equal("kuma_vet", req.Element(Merc + "initiator")!.Element(Vd + "login")!.Value);
            // Элементы идут в порядке схемы Delivery
            Assert.Equal(new[] { "deliveryDate", "consignor", "consignee", "consignment", "transportInfo", "transportStorageType", "accompanyingForms" },
                delivery.Elements().Select(e => e.Name.LocalName));
        }

        [Fact]
        public void ListRequest_FiltersIncomingConfirmedOnEnterprise()
        {
            var req = VetDocuments.ListRequest(Options, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc), 100);
            Assert.Equal("INCOMING", req.Element(Vd + "vetDocumentType")!.Value);
            Assert.Equal("CONFIRMED", req.Element(Vd + "vetDocumentStatus")!.Value);
            Assert.Equal("100", req.Element(Bs + "listOptions")!.Element(Bs + "offset")!.Value);
            Assert.Equal(Options.EnterpriseGuid, req.Element(Dt + "enterpriseGuid")!.Value);
            Assert.Equal("2026-10-01T03:00:00", req.Element(Vd + "issueDateInterval")!.Element(Bs + "beginDate")!.Value);
        }

        [Fact]
        public void Validate_RequiresCredentials()
        {
            Assert.Throws<VetisException>(() => VetisClient.Validate(new MercuryOptions()));
            VetisClient.Validate(Options);
        }
    }
}
