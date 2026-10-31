using System.Text.Json.Nodes;

namespace Cafe.API.Services.Fiscal
{
    /// <summary>Задания АТОЛ Web Server в JSON (формат ДТО 10). Без обращения к сети — удобно тестировать.</summary>
    public static class FiscalTasks
    {
        public static readonly string[] TaxationTypes = { "osn", "usnIncome", "usnIncomeOutcome", "esn", "patent" };
        public static readonly string[] VatTypes = { "none", "vat0", "vat5", "vat7", "vat10", "vat20", "vat22" };
        public static readonly string[] PaymentObjects = { "commodity", "service" };

        /// <summary>Чек прихода (sell) или возврата прихода (sellReturn)</summary>
        public static JsonObject Receipt(
            string kind, FiscalOptions o, IReadOnlyList<ReceiptLine> lines, string paymentMethod,
            string cashierName, string? clientContact, IReadOnlyDictionary<string, string>? vars = null)
        {
            var items = new JsonArray();
            foreach (var l in lines)
            {
                items.Add(new JsonObject
                {
                    ["type"] = "position",
                    ["name"] = l.Name,
                    ["price"] = l.Price,
                    ["quantity"] = l.Quantity,
                    ["amount"] = l.Amount,
                    ["paymentMethod"] = "fullPayment",
                    ["paymentObject"] = l.IsService ? "service" : o.PaymentObject,
                    ["tax"] = new JsonObject { ["type"] = o.Vat },
                });
            }
            var total = lines.Sum(l => l.Amount);

            var task = new JsonObject
            {
                ["type"] = kind,
                ["taxationType"] = o.TaxationType,
                ["operator"] = Operator(o, cashierName),
                ["items"] = items,
                ["payments"] = new JsonArray(new JsonObject
                {
                    ["type"] = paymentMethod == "cash" ? "cash" : "electronically",
                    ["sum"] = total,
                }),
                ["total"] = total,
            };
            if (!string.IsNullOrWhiteSpace(clientContact))
                task["clientInfo"] = new JsonObject { ["emailOrPhone"] = clientContact };

            // Свой текст до и после фискальной части (печатается, в ФН и ОФД не уходит)
            var layout = o.Layout ?? ReceiptLayout.Default();
            var v = vars ?? new Dictionary<string, string>();
            var pre = ReceiptLayout.Items(layout.Header, v);
            var post = ReceiptLayout.Items(layout.Footer, v, layout.Qr);
            if (pre.Count > 0) task["preItems"] = pre;
            if (post.Count > 0) task["postItems"] = post;
            return task;
        }

        /// <summary>Нефискальный документ — образец оформления (в ФН не записывается)</summary>
        public static JsonObject Sample(FiscalOptions o, ReceiptLayout layout, IReadOnlyList<ReceiptLine> lines, IReadOnlyDictionary<string, string> vars)
        {
            var items = ReceiptLayout.Items(layout.Header, vars);
            var w = layout.Width;
            items.Add(Text(new string('-', w)));
            items.Add(Text("ОБРАЗЕЦ — НЕ ЯВЛЯЕТСЯ ЧЕКОМ", "center"));
            foreach (var l in lines)
            {
                items.Add(Text(l.Name));
                items.Add(Text($"{l.Quantity:0.###} x {l.Price:0.00} = {l.Amount:0.00}", "right"));
            }
            items.Add(Text($"ИТОГ {lines.Sum(l => l.Amount):0.00}", "right"));
            items.Add(Text(new string('-', w)));
            foreach (var i in ReceiptLayout.Items(layout.Footer, vars, layout.Qr)) items.Add(i!.DeepClone());
            return new JsonObject { ["type"] = "nonFiscal", ["items"] = items };
        }

        private static JsonObject Text(string text, string align = "left") =>
            new() { ["type"] = "text", ["text"] = text, ["alignment"] = align };

        /// <summary>openShift, closeShift, reportX</summary>
        public static JsonObject Shift(string type, FiscalOptions o, string cashierName) =>
            new() { ["type"] = type, ["operator"] = Operator(o, cashierName) };

        public static JsonObject ShiftStatus() => new() { ["type"] = "getShiftStatus" };

        private static JsonObject Operator(FiscalOptions o, string cashierName)
        {
            var op = new JsonObject { ["name"] = string.IsNullOrWhiteSpace(cashierName) ? "Кассир" : Trunc(cashierName.Trim(), 64) };
            if (!string.IsNullOrWhiteSpace(o.CashierInn)) op["vatin"] = o.CashierInn.Trim();
            return op;
        }

        private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
    }
}
