using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Kumashaurma.API.Services.Egais
{
    public record WayBillPosition(
        string Identity,
        string ProductName,
        string? AlcCode,
        decimal? Capacity,
        decimal? AlcVolume,
        string? UnitType,
        decimal Quantity,
        decimal Price,
        string? F2RegId);

    public record WayBillData(
        string? Identity,
        string Number,
        DateTime? Date,
        string? ShipperFsrarId,
        string? ShipperInn,
        string? ShipperName,
        string? ConsigneeFsrarId,
        List<WayBillPosition> Positions);

    /// <summary>Справка о регистрации ТТН (FORM2REGINFO / TTNInformF2Reg)</summary>
    public record RegInfoData(
        string? Identity,
        string? WbRegId,
        string? WbNumber,
        DateTime? WbDate,
        string? ShipperFsrarId,
        Dictionary<string, string> F2RegIdByPosition);

    /// <summary>Квитанция ЕГАИС</summary>
    public record TicketData(string? RegId, string? DocType, bool? Accepted, string Comment);

    public enum ActDecision { Accepted, Rejected, Differences }

    /// <summary>Неподтверждённая накладная из ответа ЕГАИС на запрос QueryNATTN</summary>
    public record NoAnswerTtn(string WbRegId, string? Number, DateTime? Date, string? ShipperFsrarId);

    public record ActPosition(string Identity, string? InformF2RegId, decimal RealQuantity);

    /// <summary>
    /// Разбор документов УТМ и сборка акта по ТТН. Элементы ищутся по локальным именам,
    /// чтобы не зависеть от версии схемы и префиксов пространств имён.
    /// </summary>
    public static class EgaisXml
    {
        // Пространства имён акта на ТТН версии 4 (WayBillAct_v4)
        public const string NsDocuments = "http://fsrar.ru/WEGAIS/WB_DOC_SINGLE_01";
        public const string NsActV4 = "http://fsrar.ru/WEGAIS/ActTTNSingle_v4";
        public const string NsCommonV3 = "http://fsrar.ru/WEGAIS/CommonV3";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static XDocument Load(string xml)
        {
            // Без DTD и внешних сущностей
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            return XDocument.Load(reader);
        }

        private static IEnumerable<XElement> Desc(XContainer e, string name) => e.Descendants().Where(x => x.Name.LocalName == name);
        private static XElement? First(XContainer? e, string name) => e == null ? null : Desc(e, name).FirstOrDefault();
        private static XElement? Child(XElement? e, string name) => e?.Elements().FirstOrDefault(x => x.Name.LocalName == name);
        private static string? Text(XContainer? e, string name) => First(e, name)?.Value.Trim() is { Length: > 0 } v ? v : null;
        private static string? ChildText(XElement? e, string name) => Child(e, name)?.Value.Trim() is { Length: > 0 } v ? v : null;

        private static decimal? Dec(string? s) =>
            decimal.TryParse(s?.Replace(',', '.'), NumberStyles.Number, Inv, out var v) ? v : null;

        private static DateTime? Date(string? s) =>
            DateTime.TryParse(s, Inv, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : null;

        /// <summary>Тип документа по корневому элементу внутри Document (WayBill_v4, TTNInformF2Reg, Ticket…)</summary>
        public static string? DetectType(XDocument doc)
        {
            var document = First(doc, "Document");
            return document?.Elements().FirstOrDefault()?.Name.LocalName ?? doc.Root?.Name.LocalName;
        }

        public static WayBillData ParseWayBill(XDocument doc)
        {
            var wb = Desc(doc, "WayBill_v4").FirstOrDefault()
                     ?? Desc(doc, "WayBill_v3").FirstOrDefault()
                     ?? Desc(doc, "WayBill").FirstOrDefault()
                     ?? throw new FormatException("В документе нет накладной (WayBill)");

            var header = Child(wb, "Header");
            var shipper = Child(header, "Shipper");
            var consignee = Child(header, "Consignee");

            var positions = new List<WayBillPosition>();
            foreach (var p in Desc(Child(wb, "Content") ?? wb, "Position").Where(p => Child(p, "Product") != null))
            {
                var product = Child(p, "Product");
                positions.Add(new WayBillPosition(
                    Identity: ChildText(p, "Identity") ?? (positions.Count + 1).ToString(Inv),
                    ProductName: Text(product, "FullName") ?? Text(product, "ShortName") ?? "Без названия",
                    AlcCode: Text(product, "AlcCode"),
                    Capacity: Dec(Text(product, "Capacity")),
                    AlcVolume: Dec(Text(product, "AlcVolume")),
                    UnitType: Text(product, "UnitType"),
                    Quantity: Dec(ChildText(p, "Quantity")) ?? 0,
                    Price: Dec(ChildText(p, "Price")) ?? 0,
                    F2RegId: Text(Child(p, "InformF2"), "F2RegId") ?? Text(p, "F2RegId")));
            }

            return new WayBillData(
                Identity: ChildText(wb, "Identity"),
                Number: ChildText(header, "NUMBER") ?? ChildText(header, "Number") ?? "",
                Date: Date(ChildText(header, "Date")),
                ShipperFsrarId: Text(shipper, "ClientRegId"),
                ShipperInn: Text(shipper, "INN"),
                ShipperName: Text(shipper, "ShortName") ?? Text(shipper, "FullName"),
                ConsigneeFsrarId: Text(consignee, "ClientRegId"),
                Positions: positions);
        }

        public static RegInfoData ParseRegInfo(XDocument doc)
        {
            var root = Desc(doc, "TTNInformF2Reg").FirstOrDefault()
                       ?? Desc(doc, "TTNInformBReg").FirstOrDefault()
                       ?? throw new FormatException("В документе нет справки о регистрации ТТН");
            var header = Child(root, "Header");

            var map = new Dictionary<string, string>();
            foreach (var p in Desc(Child(root, "Content") ?? root, "Position"))
            {
                var id = ChildText(p, "Identity");
                var reg = ChildText(p, "InformF2RegId") ?? ChildText(p, "InformBRegId") ?? Text(p, "F2RegId");
                if (id != null && reg != null) map[id] = reg;
            }

            return new RegInfoData(
                Identity: ChildText(header, "Identity"),
                WbRegId: ChildText(header, "WBRegId"),
                WbNumber: ChildText(header, "WBNUMBER"),
                WbDate: Date(ChildText(header, "WBDate")),
                ShipperFsrarId: Text(Child(header, "Shipper"), "ClientRegId"),
                F2RegIdByPosition: map);
        }

        /// <summary>Ответ на QueryNATTN — список накладных, по которым ещё не отправлен акт</summary>
        public static List<NoAnswerTtn> ParseNoAnswer(XDocument doc) =>
            Desc(doc, "NoAnswer")
                .Select(n => new NoAnswerTtn(
                    ChildText(n, "WbRegID") ?? ChildText(n, "WBRegId") ?? "",
                    ChildText(n, "ttnNumber"),
                    Date(ChildText(n, "ttnDate")),
                    ChildText(n, "Shipper")))
                .Where(n => n.WbRegId.Length > 0)
                .ToList();

        public const string NsQueryParameters = "http://fsrar.ru/WEGAIS/QueryParameters";

        /// <summary>Запрос с параметрами: QueryNATTN (КОД = ФСРАР ID) или QueryResendDoc (WBREGID = номер ТТН)</summary>
        public static string BuildQuery(string fsrarId, string queryName, string paramName, string paramValue)
        {
            XNamespace ns = NsDocuments, qp = NsQueryParameters;
            var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
                new XElement(ns + "Documents",
                    new XAttribute("Version", "1.0"),
                    new XAttribute(XNamespace.Xmlns + "ns", ns),
                    new XAttribute(XNamespace.Xmlns + "qp", qp),
                    new XElement(ns + "Owner", new XElement(ns + "FSRAR_ID", fsrarId)),
                    new XElement(ns + "Document",
                        new XElement(ns + queryName,
                            new XElement(qp + "Parameters",
                                new XElement(qp + "Parameter",
                                    new XElement(qp + "Name", paramName),
                                    new XElement(qp + "Value", paramValue)))))));
            var sb = new StringBuilder();
            using (var writer = new Utf8StringWriter(sb))
                doc.Save(writer, SaveOptions.None);
            return sb.ToString();
        }

        public static TicketData ParseTicket(XDocument doc)
        {
            var ticket = Desc(doc, "Ticket").FirstOrDefault() ?? throw new FormatException("Это не квитанция");
            var regId = ChildText(ticket, "RegID");
            var docType = ChildText(ticket, "DocType");

            var result = Child(ticket, "Result");
            if (result != null)
            {
                var conclusion = ChildText(result, "Conclusion");
                return new TicketData(regId, docType, conclusion == null ? null : conclusion == "Accepted", ChildText(result, "Comments") ?? conclusion ?? "");
            }

            var op = Child(ticket, "OperationResult");
            var opResult = ChildText(op, "OperationResult");
            return new TicketData(regId, docType, opResult == null ? null : opResult == "Accepted",
                ChildText(op, "OperationComment") ?? opResult ?? "");
        }

        /// <summary>
        /// Акт на ТТН v4: Accepted — принять, Rejected — отказать, Differences — принять с расхождениями
        /// (для каждой позиции — фактическое количество).
        /// </summary>
        public static string BuildWayBillAct(string fsrarId, ActDecision decision, string actNumber, DateTime actDate,
            string wbRegId, string? note, IEnumerable<ActPosition>? positions)
        {
            XNamespace ns = NsDocuments, wa = NsActV4, ce = NsCommonV3;
            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

            var header = new XElement(wa + "Header",
                new XElement(wa + "IsAccept", decision.ToString()),
                new XElement(wa + "ACTNUMBER", actNumber),
                new XElement(wa + "ActDate", actDate.ToString("yyyy-MM-dd", Inv)),
                new XElement(wa + "WBRegId", wbRegId));
            if (!string.IsNullOrWhiteSpace(note))
                header.Add(new XElement(wa + "Note", note.Length > 500 ? note[..500] : note));

            var content = new XElement(wa + "Content");
            if (decision == ActDecision.Differences && positions != null)
            {
                foreach (var p in positions)
                {
                    var position = new XElement(wa + "Position",
                        new XElement(wa + "Identity", p.Identity));
                    if (!string.IsNullOrEmpty(p.InformF2RegId))
                        position.Add(new XElement(wa + "InformF2RegId", p.InformF2RegId));
                    position.Add(new XElement(wa + "RealQuantity", p.RealQuantity.ToString("0.####", Inv)));
                    content.Add(position);
                }
            }

            var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
                new XElement(ns + "Documents",
                    new XAttribute("Version", "1.0"),
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(XNamespace.Xmlns + "ns", ns),
                    new XAttribute(XNamespace.Xmlns + "wa", wa),
                    new XAttribute(XNamespace.Xmlns + "ce", ce),
                    new XElement(ns + "Owner", new XElement(ns + "FSRAR_ID", fsrarId)),
                    new XElement(ns + "Document", new XElement(ns + "WayBillAct_v4", header, content))));

            var sb = new StringBuilder();
            using (var writer = new Utf8StringWriter(sb))
                doc.Save(writer, SaveOptions.None);
            return sb.ToString();
        }

        private sealed class Utf8StringWriter : StringWriter
        {
            public Utf8StringWriter(StringBuilder sb) : base(sb, Inv) { }
            public override Encoding Encoding => Encoding.UTF8;
        }
    }
}
