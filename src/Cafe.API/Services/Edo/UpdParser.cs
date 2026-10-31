using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Cafe.API.Services.Edo
{
    public class UpdLine
    {
        public int No { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>Артикул / код товара поставщика</summary>
        public string? Code { get; set; }
        public string? Gtin { get; set; }
        /// <summary>Код единицы по ОКЕИ: 796 шт, 166 кг, 163 г, 112 л, 111 мл</summary>
        public string? Okei { get; set; }
        public string? UnitName { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal SumWithoutVat { get; set; }
        public string? VatRate { get; set; }
        /// <summary>Сумма НДС по строке (0 — без НДС или не указана)</summary>
        public decimal Vat { get; set; }
        /// <summary>Строка помечена «без НДС»</summary>
        public bool WithoutVat { get; set; }
        public decimal Sum { get; set; }
        /// <summary>Коды маркировки (КИЗ / упаковки) из накладной</summary>
        public List<string> Codes { get; set; } = new();

        /// <summary>Ключ для запоминания сопоставления: GTIN надёжнее всего, затем артикул, затем название</summary>
        public string Key =>
            !string.IsNullOrEmpty(Gtin) ? $"gtin:{Gtin}"
            : !string.IsNullOrWhiteSpace(Code) ? $"code:{Code.Trim()}"
            : $"name:{string.Join(' ', Name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))}";
    }

    public class UpdDocument
    {
        public string? FileId { get; set; }
        public string? Number { get; set; }
        public DateTime? Date { get; set; }
        public string? SellerInn { get; set; }
        public string? SellerKpp { get; set; }
        public string? SellerName { get; set; }
        public string? BuyerInn { get; set; }
        public decimal Total { get; set; }
        /// <summary>Сумма НДС по накладной (из итогов УПД, иначе сумма по строкам)</summary>
        public decimal VatTotal { get; set; }
        /// <summary>Накладная целиком без НДС (продавец не плательщик НДС)</summary>
        public bool WithoutVat { get; set; }
        /// <summary>Банковские реквизиты продавца (есть не во всех УПД)</summary>
        public UpdBank? SellerBank { get; set; }
        /// <summary>Основание передачи товара: договор, заказ, накладная</summary>
        public UpdBasis? Basis { get; set; }
        /// <summary>Отсрочка платежа в днях — из доп. сведений поставщика («Отсрочка: 14 дн.»)</summary>
        public int? PaymentDelayDays { get; set; }
        /// <summary>Срок оплаты, если поставщик указал его датой</summary>
        public DateTime? ExplicitDueDate { get; set; }
        public List<UpdLine> Lines { get; set; } = new();

        /// <summary>Срок оплаты: явная дата поставщика, иначе дата накладной + отсрочка; null — срок не указан</summary>
        public DateTime? DueDate =>
            ExplicitDueDate ?? (Date.HasValue && PaymentDelayDays.HasValue ? Date.Value.AddDays(PaymentDelayDays.Value) : null);
    }

    public record UpdBank(string? Account, string? Bik, string? CorrAccount, string? BankName);

    public record UpdBasis(string? Name, string? Number, DateTime? Date)
    {
        public bool IsContract => Name != null && Name.Contains("договор", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>НДС по одной ставке: сумма с НДС и сам налог</summary>
    public record UpdVatGroup(string Rate, decimal Sum, decimal Vat);

    public class UpdParseException : Exception
    {
        public UpdParseException(string message) : base(message) { }
    }

    /// <summary>
    /// Разбор УПД в формате ФНС (ON_NSCHFDOPPR, версии 5.01 и 5.03).
    /// Элементы ищем по локальному имени — пространств имён в этих файлах нет, но так надёжнее.
    /// </summary>
    public static class UpdParser
    {
        static UpdParser() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        /// <summary>Файл из СБИС → строка (файлы ФНС обычно в windows-1251, кодировка указана в заголовке XML)</summary>
        public static string DecodeFile(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            var doc = XDocument.Load(stream, LoadOptions.None);
            // Храним без объявления кодировки — дальше это обычная строка
            return doc.Root?.ToString() ?? string.Empty;
        }

        public static bool IsUpdFileName(string? name) =>
            !string.IsNullOrEmpty(name)
            && name.StartsWith("ON_NSCHFDOPPR", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

        public static UpdDocument Parse(string xml)
        {
            XDocument x;
            try { x = XDocument.Parse(xml); }
            catch (System.Xml.XmlException ex) { throw new UpdParseException($"Файл не похож на XML: {ex.Message}"); }

            var root = x.Root ?? throw new UpdParseException("Пустой файл");
            var document = Child(root, "Документ") ?? throw new UpdParseException("В файле нет элемента «Документ» — это не УПД");
            var info = Child(document, "СвСчФакт") ?? throw new UpdParseException("В файле нет сведений о счёте-фактуре (СвСчФакт)");

            var result = new UpdDocument
            {
                FileId = Attr(root, "ИдФайл"),
                // 5.01: НомерСчФ / ДатаСчФ; 5.03: НомерДок / ДатаДок
                Number = Attr(info, "НомерСчФ") ?? Attr(info, "НомерДок"),
                Date = ParseDate(Attr(info, "ДатаСчФ") ?? Attr(info, "ДатаДок")),
            };

            var seller = Child(info, "СвПрод");
            if (seller != null)
            {
                (result.SellerInn, result.SellerKpp, result.SellerName) = Party(seller);
                result.SellerBank = Bank(seller);
            }
            var buyer = Child(info, "СвПокуп");
            if (buyer != null) result.BuyerInn = Party(buyer).Inn;

            var table = Child(document, "ТаблСчФакт") ?? throw new UpdParseException("В накладной нет таблицы товаров");
            var no = 0;
            foreach (var row in Children(table, "СведТов"))
            {
                no++;
                var extra = Child(row, "ДопСведТов");
                var line = new UpdLine
                {
                    No = int.TryParse(Attr(row, "НомСтр"), out var n) ? n : no,
                    Name = (Attr(row, "НаимТов") ?? "").Trim(),
                    Okei = Attr(row, "ОКЕИ_Тов"),
                    UnitName = Attr(row, "НаимЕдИзм") ?? (extra != null ? Attr(extra, "НаимЕдИзм") : null),
                    Quantity = Dec(Attr(row, "КолТов")),
                    Price = Dec(Attr(row, "ЦенаТов")),
                    SumWithoutVat = Dec(Attr(row, "СтТовБезНДС")),
                    VatRate = Attr(row, "НалСт"),
                    Sum = Dec(Attr(row, "СтТовУчНал")),
                    Code = extra != null ? Attr(extra, "КодТов") : null,
                    Gtin = extra != null ? Attr(extra, "ГТИН") : null,
                };
                if (line.Sum == 0) line.Sum = line.SumWithoutVat;
                if (line.Sum == 0 && line.Price > 0) line.Sum = Math.Round(line.Price * line.Quantity, 2);
                (line.Vat, line.WithoutVat) = ParseVat(Child(row, "СумНал"), line.VatRate);
                // Сумма налога не указана, но есть суммы с НДС и без — налог как разница
                if (line.Vat == 0 && !line.WithoutVat && line.SumWithoutVat > 0 && line.Sum > line.SumWithoutVat)
                    line.Vat = line.Sum - line.SumWithoutVat;

                if (extra != null)
                    foreach (var ids in Children(extra, "НомСредИдентТов"))
                        foreach (var el in ids.Elements())
                            if (el.Name.LocalName is "КИЗ" or "НомУпак" or "ИдентТрансУпак" && !string.IsNullOrWhiteSpace(el.Value))
                                line.Codes.Add(el.Value.Trim());

                // GTIN из первого кода маркировки: 01 + 14 цифр
                if (string.IsNullOrEmpty(line.Gtin))
                {
                    var c = line.Codes.FirstOrDefault(c => c.Length >= 16 && c.StartsWith("01") && c[2..16].All(char.IsDigit));
                    if (c != null) line.Gtin = c[2..16];
                }
                result.Lines.Add(line);
            }

            var totals = Child(table, "ВсегоОпл");
            result.Total = totals != null ? Dec(Attr(totals, "СтТовУчНалВсего")) : 0;
            if (result.Total == 0) result.Total = result.Lines.Sum(l => l.Sum);

            ParsePaymentTerms(document, info, result);

            var (vatTotal, totalWithoutVat) = ParseVat(totals != null ? Child(totals, "СумНалВсего") : null, null);
            result.VatTotal = vatTotal > 0 ? vatTotal : result.Lines.Sum(l => l.Vat);
            result.WithoutVat = result.VatTotal == 0 && (totalWithoutVat || (result.Lines.Count > 0 && result.Lines.All(l => l.WithoutVat)));
            return result;
        }

        // Максимум отсрочки, который считаем правдоподобным: всё больше — скорее опечатка или не дни
        private const int MaxPaymentDelayDays = 365;

        /// <summary>
        /// Условия оплаты. В формате ФНС для них нет отдельного поля, поэтому поставщики пишут их в доп. сведения:
        /// по строкам (ИнфПолФХЖ2 «Отсрочка» = «14 дн.») или по документу (ИнфПолФХЖ1). Основание — СвПродПер/СвПер/ОснПер.
        /// </summary>
        private static void ParsePaymentTerms(XElement document, XElement info, UpdDocument result)
        {
            var basis = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "ОснПер");
            if (basis != null)
                result.Basis = new UpdBasis(Attr(basis, "РеквНаимДок"), Attr(basis, "РеквНомерДок"), ParseDate(Attr(basis, "РеквДатаДок")));

            var notes = info.Descendants().Where(e => e.Name.LocalName == "ТекстИнф")
                .Concat(document.Descendants().Where(e => e.Name.LocalName == "ИнфПолФХЖ2"))
                .Select(e => (Key: (Attr(e, "Идентиф") ?? "").ToLowerInvariant(), Value: Attr(e, "Значен") ?? ""));

            foreach (var (key, value) in notes)
            {
                if (key.Contains("отсрочк"))
                {
                    var days = LeadingNumber(value);
                    if (days is > 0 and <= MaxPaymentDelayDays && days > (result.PaymentDelayDays ?? 0))
                        result.PaymentDelayDays = days;
                }
                else if ((key.Contains("срок") || key.Contains("дата")) && key.Contains("оплат"))
                {
                    result.ExplicitDueDate ??= ParseDate(value.Trim());
                }
            }
        }

        /// <summary>«14 дн.» → 14</summary>
        private static int? LeadingNumber(string value)
        {
            var digits = new string(value.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, out var n) ? n : null;
        }

        private static UpdBank? Bank(XElement party)
        {
            var bank = Child(party, "БанкРекв");
            if (bank == null) return null;
            var info = Child(bank, "СвБанк");
            var result = new UpdBank(
                Attr(bank, "НомерСчета"),
                info != null ? Attr(info, "БИК") : null,
                info != null ? Attr(info, "КорСчет") : null,
                info != null ? Attr(info, "НаимБанк") : null);
            return result.Account == null && result.Bik == null ? null : result;
        }

        /// <summary>
        /// Блок «СумНал»: внутри либо СумНал (сумма), либо БезНДС («без НДС»), либо ДефНДС («-», не указано).
        /// </summary>
        private static (decimal Vat, bool WithoutVat) ParseVat(XElement? holder, string? rate)
        {
            if (holder != null)
            {
                var amount = Child(holder, "СумНал");
                if (amount != null) return (Dec(amount.Value), false);
                if (Child(holder, "БезНДС") != null) return (0, true);
            }
            return (0, rate != null && rate.Contains("без", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>НДС по ставкам (только строки, где налог есть)</summary>
        public static List<UpdVatGroup> VatByRate(UpdDocument doc) =>
            doc.Lines
                .Where(l => l.Vat > 0)
                .GroupBy(l => NormalizeRate(l.VatRate))
                .Select(g => new UpdVatGroup(g.Key, g.Sum(l => l.Sum), g.Sum(l => l.Vat)))
                .OrderByDescending(g => g.Vat)
                .ToList();

        private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

        // Разделитель тысяч в ru-RU — неразрывный (или узкий неразрывный) пробел; в платёжке нужен обычный
        private static string Money(decimal value) => value.ToString("N2", Ru).Replace('\u00A0', ' ').Replace('\u202F', ' ') + " руб.";

        /// <summary>«в т.ч. НДС 20% — 1 234,56 руб.» / «НДС не облагается» — как пишут в назначении платежа</summary>
        public static string VatPhrase(UpdDocument doc)
        {
            if (doc.WithoutVat) return "НДС не облагается";
            if (doc.VatTotal == 0) return "НДС не указан";
            var groups = VatByRate(doc);
            if (groups.Count <= 1)
            {
                var rate = groups.FirstOrDefault()?.Rate;
                return string.IsNullOrEmpty(rate) ? $"в т.ч. НДС {Money(doc.VatTotal)}" : $"в т.ч. НДС {rate} — {Money(doc.VatTotal)}";
            }
            return "в т.ч. НДС " + string.Join(", ", groups.Select(g => $"{g.Rate} — {Money(g.Vat)}"));
        }

        /// <summary>Готовое назначение платежа для платёжки по накладной (с договором, если он указан основанием)</summary>
        public static string PaymentPurpose(UpdDocument doc)
        {
            var number = string.IsNullOrWhiteSpace(doc.Number) ? "" : $" № {doc.Number}";
            var date = doc.Date.HasValue ? $" от {doc.Date.Value:dd.MM.yyyy}" : "";
            var contract = "";
            if (doc.Basis is { IsContract: true } b && !string.IsNullOrWhiteSpace(b.Number))
                // Некоторые поставщики пишут номер уже со знаком «№» — не удваиваем
                contract = $" по договору № {b.Number.Trim().TrimStart('№').Trim()}" + (b.Date.HasValue ? $" от {b.Date.Value:dd.MM.yyyy}" : "");
            return $"Оплата по УПД{number}{date}{contract}. Сумма {Money(doc.Total)}, {VatPhrase(doc)}";
        }

        /// <summary>«20%», «10%», «20/120», «без НДС»; пустая ставка — ""</summary>
        private static string NormalizeRate(string? rate) => (rate ?? "").Trim().Replace(" %", "%");

        /// <summary>Сколько единиц склада в одной единице накладной — по коду ОКЕИ (если единицы совместимы)</summary>
        public static decimal DefaultFactor(string? okei, string stockUnit) => (okei, stockUnit) switch
        {
            ("163", "kg") => 0.001m,  // граммы → кг
            ("111", "l") => 0.001m,   // мл → л
            ("168", "kg") => 1000m,   // тонны → кг
            _ => 1m
        };

        private static (string? Inn, string? Kpp, string? Name) Party(XElement party)
        {
            var id = Child(party, "ИдСв");
            if (id == null) return (null, null, null);
            var org = Child(id, "СвЮЛУч");
            if (org != null) return (Attr(org, "ИННЮЛ"), Attr(org, "КПП"), Attr(org, "НаимОрг"));
            var ip = Child(id, "СвИП");
            if (ip != null)
            {
                var fio = Child(ip, "ФИО");
                var name = fio == null ? null
                    : $"ИП {Attr(fio, "Фамилия")} {Attr(fio, "Имя")} {Attr(fio, "Отчество")}".Trim();
                return (Attr(ip, "ИННФЛ"), null, name);
            }
            var foreign = Child(id, "СвИнНеУч");
            return foreign != null ? (null, null, Attr(foreign, "НаимОрг")) : (null, null, null);
        }

        private static XElement? Child(XElement e, string name) => e.Elements().FirstOrDefault(c => c.Name.LocalName == name);
        private static IEnumerable<XElement> Children(XElement e, string name) => e.Elements().Where(c => c.Name.LocalName == name);
        private static string? Attr(XElement e, string name) =>
            e.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value is { Length: > 0 } v ? v : null;

        private static decimal Dec(string? s) =>
            decimal.TryParse(s?.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0;

        private static DateTime? ParseDate(string? s) =>
            DateTime.TryParseExact(s, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
                : null;
    }
}
