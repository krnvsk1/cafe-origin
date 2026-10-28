using System.Globalization;
using System.Xml.Linq;
using static Kumashaurma.API.Services.Mercury.VetisClient;

namespace Kumashaurma.API.Services.Mercury
{
    public record VetDocumentInfo(
        string Uuid,
        DateTime? IssueDate,
        string? Type,
        string? Status,
        string? ProductName,
        decimal Volume,
        string? UnitName,
        string? ProductionDate,
        string? ExpiryDate,
        string? ConsignorName,
        string? Waybill,
        XElement Element);

    /// <summary>Сборка запросов к Меркурию и разбор ВСД (без обращения к сети — удобно тестировать)</summary>
    public static class VetDocuments
    {
        /// <summary>Входящие оформленные ВСД на площадку за интервал дат (Меркурий — не больше 31 дня за запрос)</summary>
        public static XElement ListRequest(MercuryOptions o, DateTime fromUtc, DateTime toUtc, int offset, int count = 100) =>
            new(Merc + "getVetDocumentListRequest",
                new XElement(Merc + "localTransactionId", Guid.NewGuid().ToString("N")[..20]),
                new XElement(Merc + "initiator", new XElement(Vd + "login", o.InitiatorLogin.Trim())),
                new XElement(Bs + "listOptions",
                    new XElement(Bs + "count", count),
                    new XElement(Bs + "offset", offset)),
                new XElement(Vd + "vetDocumentType", "INCOMING"),
                new XElement(Vd + "vetDocumentStatus", "CONFIRMED"),
                new XElement(Vd + "issueDateInterval",
                    new XElement(Bs + "beginDate", Moscow(fromUtc)),
                    new XElement(Bs + "endDate", Moscow(toUtc))),
                new XElement(Dt + "enterpriseGuid", o.EnterpriseGuid.Trim()));

        public static (List<VetDocumentInfo> Docs, int Total) ParseList(XElement response)
        {
            var list = response.Descendants().FirstOrDefault(e => e.Name.LocalName == "vetDocumentList");
            if (list == null) return (new(), 0);
            var total = int.TryParse(list.Attribute("total")?.Value, out var t) ? t : 0;
            var docs = list.Elements().Where(e => e.Name.LocalName == "vetDocument").Select(Parse).ToList();
            return (docs, Math.Max(total, docs.Count));
        }

        public static VetDocumentInfo Parse(XElement doc)
        {
            var cc = Child(doc, "certifiedConsignment");
            var batch = cc != null ? Child(cc, "batch") : null;
            var consignor = cc != null ? Child(cc, "consignor") : null;
            var waybill = doc.Elements().Where(e => e.Name.LocalName == "referencedDocument")
                .FirstOrDefault(r => Child(r, "type")?.Value is "1" or "2" or "3" or "4" or "5");

            return new VetDocumentInfo(
                Uuid: Child(doc, "uuid")?.Value ?? "",
                IssueDate: DateTime.TryParse(Child(doc, "issueDate")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    ? DateTime.SpecifyKind(d.Date, DateTimeKind.Utc) : null,
                Type: Child(doc, "vetDType")?.Value,
                Status: Child(doc, "vetDStatus")?.Value,
                ProductName: Name(batch == null ? null : Child(batch, "productItem")) ?? Name(batch == null ? null : Child(batch, "subProduct")),
                Volume: decimal.TryParse(batch == null ? null : Child(batch, "volume")?.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0,
                UnitName: Name(batch == null ? null : Child(batch, "unit")),
                ProductionDate: GoodsDate(batch == null ? null : Child(batch, "dateOfProduction")),
                ExpiryDate: GoodsDate(batch == null ? null : Child(batch, "expiryDate")),
                ConsignorName: Name(consignor == null ? null : Child(consignor, "businessEntity")) ?? Name(consignor == null ? null : Child(consignor, "enterprise")),
                Waybill: waybill == null ? null
                    : $"№ {Child(waybill, "issueSeries")?.Value}{Child(waybill, "issueNumber")?.Value} от {FormatDate(Child(waybill, "issueDate")?.Value)}".Trim(),
                Element: doc);
        }

        /// <summary>
        /// Гашение ВСД целиком (ACCEPT_ALL): партия принимается как в документе — данные копируются из самого ВСД.
        /// Если фактически пришло другое количество, гасить нужно в Меркурии с актом несоответствия.
        /// </summary>
        public static XElement AcceptRequest(MercuryOptions o, XElement vetDocument, DateTime nowUtc)
        {
            var uuid = Child(vetDocument, "uuid")?.Value ?? throw new VetisException("В ВСД нет UUID");
            var cc = Child(vetDocument, "certifiedConsignment") ?? throw new VetisException("В ВСД нет сведений о партии (certifiedConsignment)");
            var batch = Child(cc, "batch") ?? throw new VetisException("В ВСД нет сведений о продукции (batch)");
            var consignor = Child(cc, "consignor") ?? throw new VetisException("В ВСД нет отправителя");
            var consignee = Child(cc, "consignee") ?? throw new VetisException("В ВСД нет получателя");
            var login = o.InitiatorLogin.Trim();

            var delivery = new XElement(Merc + "delivery",
                new XElement(Vd + "deliveryDate", Moscow(nowUtc)),
                new XElement(Vd + "consignor", consignor.Elements().Select(Copy)),
                new XElement(Vd + "consignee", consignee.Elements().Select(Copy)),
                new XElement(Vd + "consignment", batch.Elements().Select(Copy)));
            if (Child(cc, "transportInfo") is { } transport) delivery.Add(new XElement(Vd + "transportInfo", transport.Elements().Select(Copy)));
            if (Child(cc, "transportStorageType") is { } storage) delivery.Add(new XElement(Vd + "transportStorageType", storage.Value));

            var forms = new XElement(Vd + "accompanyingForms");
            var waybill = vetDocument.Elements().Where(e => e.Name.LocalName == "referencedDocument")
                .FirstOrDefault(r => Child(r, "type")?.Value is "1" or "2" or "3" or "4" or "5");
            if (waybill != null)
                forms.Add(new XElement(Vd + "waybill",
                    new[] { "issueSeries", "issueNumber", "issueDate", "type" }
                        .Select(n => Child(waybill, n)).Where(e => e != null).Select(e => new XElement(Vd + e!.Name.LocalName, e.Value))));
            forms.Add(new XElement(Vd + "vetCertificate", new XElement(Bs + "uuid", uuid)));
            delivery.Add(forms);

            return new XElement(Merc + "processIncomingConsignmentRequest",
                new XElement(Merc + "localTransactionId", Guid.NewGuid().ToString("N")[..20]),
                new XElement(Merc + "initiator", new XElement(Vd + "login", login)),
                delivery,
                new XElement(Merc + "deliveryFacts",
                    new XElement(Vd + "vetCertificatePresence", "ELECTRONIC"),
                    new XElement(Vd + "docInspection",
                        new XElement(Vd + "responsible", new XElement(Vd + "login", login)),
                        new XElement(Vd + "result", "CORRESPONDS")),
                    new XElement(Vd + "vetInspection",
                        new XElement(Vd + "responsible", new XElement(Vd + "login", login)),
                        new XElement(Vd + "result", "CORRESPONDS")),
                    new XElement(Vd + "decision", "ACCEPT_ALL")));
        }

        /// <summary>GUID записи складского журнала из ответа на гашение</summary>
        public static string? StockEntryGuid(XElement response) =>
            response.Descendants().FirstOrDefault(e => e.Name.LocalName == "stockEntry") is { } se ? Child(se, "guid")?.Value : null;

        private static XElement Copy(XElement e) => new(e);

        private static XElement? Child(XElement e, string localName) => e.Elements().FirstOrDefault(c => c.Name.LocalName == localName);

        private static string? Name(XElement? e) =>
            e == null ? null : e.Elements().FirstOrDefault(c => c.Name.LocalName == "name")?.Value is { Length: > 0 } n ? n.Trim() : null;

        /// <summary>GoodsDate: firstDate (год, месяц, день[, час]) или secondDate; бывает informalDate</summary>
        private static string? GoodsDate(XElement? e)
        {
            if (e == null) return null;
            var first = Child(e, "firstDate");
            if (first != null)
            {
                var y = Child(first, "year")?.Value;
                var m = Child(first, "month")?.Value;
                var d = Child(first, "day")?.Value;
                var text = string.Join(".", new[] { d, m, y }.Where(x => !string.IsNullOrEmpty(x)).Select(x => x!.PadLeft(2, '0')));
                var second = Child(e, "secondDate");
                if (second != null)
                {
                    var t2 = string.Join(".", new[] { Child(second, "day")?.Value, Child(second, "month")?.Value, Child(second, "year")?.Value }
                        .Where(x => !string.IsNullOrEmpty(x)).Select(x => x!.PadLeft(2, '0')));
                    if (t2.Length > 0) text += $" — {t2}";
                }
                return text.Length > 0 ? text : null;
            }
            return Child(e, "informalDate")?.Value;
        }

        private static string? FormatDate(string? s) =>
            DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("dd.MM.yyyy") : s;
    }
}
