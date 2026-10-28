using System.Text.Json.Nodes;

namespace Kumashaurma.API.Services.Fiscal
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
            string cashierName, string? clientContact)
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
            return task;
        }

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
