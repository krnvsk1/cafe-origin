using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Cafe.API.Services.Fiscal
{
    /// <summary>Строка, которую касса печатает до или после фискальной части чека</summary>
    public class LayoutLine
    {
        public string Text { get; set; } = string.Empty;
        /// <summary>left, center, right</summary>
        public string Align { get; set; } = "center";
        /// <summary>Крупный шрифт (двойная ширина и высота)</summary>
        public bool Large { get; set; }
    }

    /// <summary>
    /// Оформление чека. Обязательные реквизиты (продавец, ИНН, ФН, ФД, ФП, QR ФНС) касса печатает сама —
    /// их менять нельзя. Здесь — свой текст сверху и снизу и QR-код (например, на отзыв или меню).
    /// В тексте можно использовать {order}, {type}, {cashier}, {date}, {time}.
    /// </summary>
    public class ReceiptLayout
    {
        public const int MaxLines = 12;
        public const int MaxText = 64;

        /// <summary>Символов в строке: 32 — лента 57 мм, 48 — 80 мм (для предпросмотра)</summary>
        public int Width { get; set; } = 48;
        public List<LayoutLine> Header { get; set; } = new();
        public List<LayoutLine> Footer { get; set; } = new();
        /// <summary>Ссылка или текст для QR-кода внизу чека; пусто — без QR</summary>
        public string? Qr { get; set; }

        public static ReceiptLayout Default() => new()
        {
            Header = new()
            {
                new() { Text = "КУМА ШАУРМА", Align = "center", Large = true },
                new() { Text = "Заказ № {order} · {type}", Align = "center", Large = false },
            },
            Footer = new()
            {
                new() { Text = "Спасибо! Приходите ещё", Align = "center" },
            }
        };

        public static readonly string[] Aligns = { "left", "center", "right" };

        public string? Validate()
        {
            if (Width is not (32 or 42 or 48)) return "Ширина ленты — 32, 42 или 48 символов";
            if (Header.Count > MaxLines || Footer.Count > MaxLines) return $"Не больше {MaxLines} строк сверху и снизу";
            foreach (var l in Header.Concat(Footer))
            {
                if ((l.Text ?? "").Length > MaxText) return $"Строка длиннее {MaxText} символов: «{l.Text![..20]}…»";
                if (!Aligns.Contains(l.Align)) return "Неизвестное выравнивание";
            }
            if (Qr != null && Qr.Length > 300) return "Текст QR-кода — не длиннее 300 символов";
            return null;
        }

        /// <summary>Убрать пустые строки в конце, обрезать пробелы</summary>
        public ReceiptLayout Normalized() => new()
        {
            Width = Width,
            Header = Header.Select(Clean).ToList(),
            Footer = Footer.Select(Clean).ToList(),
            Qr = string.IsNullOrWhiteSpace(Qr) ? null : Qr.Trim()
        };

        private static LayoutLine Clean(LayoutLine l) => new() { Text = (l.Text ?? "").TrimEnd(), Align = l.Align, Large = l.Large };

        private static readonly Regex Placeholder = new(@"\{(order|type|cashier|date|time)\}", RegexOptions.Compiled);

        public static string Fill(string text, IReadOnlyDictionary<string, string> vars) =>
            Placeholder.Replace(text, m => vars.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

        /// <summary>Элементы АТОЛ (preItems / postItems): текст и QR-код</summary>
        public static JsonArray Items(IEnumerable<LayoutLine> lines, IReadOnlyDictionary<string, string> vars, string? qr = null)
        {
            var arr = new JsonArray();
            foreach (var l in lines)
            {
                var item = new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = Fill(l.Text, vars),
                    ["alignment"] = l.Align,
                };
                if (l.Large)
                {
                    item["doubleWidth"] = true;
                    item["doubleHeight"] = true;
                }
                arr.Add(item);
            }
            if (!string.IsNullOrWhiteSpace(qr))
                arr.Add(new JsonObject
                {
                    ["type"] = "barcode",
                    ["barcode"] = Fill(qr, vars),
                    ["barcodeType"] = "QR",
                    ["alignment"] = "center",
                    ["scale"] = 4,
                });
            return arr;
        }
    }
}
