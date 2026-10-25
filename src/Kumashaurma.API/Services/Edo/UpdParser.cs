using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Kumashaurma.API.Services.Edo
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
        public List<UpdLine> Lines { get; set; } = new();
    }

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
            if (seller != null) (result.SellerInn, result.SellerKpp, result.SellerName) = Party(seller);
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
            return result;
        }

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
