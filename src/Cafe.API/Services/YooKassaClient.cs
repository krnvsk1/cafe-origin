using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Cafe.API.Options;

namespace Cafe.API.Services
{
    /// <summary>
    /// Клиент API ЮKassa v3 (https://yookassa.ru/developers/api) без сторонних пакетов
    /// </summary>
    public class YooKassaClient
    {
        private const string BaseUrl = "https://api.yookassa.ru/v3/";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly HttpClient _http;
        private readonly YooKassaOptions _options;
        private readonly ILogger<YooKassaClient> _logger;

        public YooKassaClient(HttpClient http, IOptions<YooKassaOptions> options, ILogger<YooKassaClient> logger)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;

            if (_options.Enabled)
            {
                var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ShopId}:{_options.SecretKey}"));
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
            }
        }

        public bool Enabled => _options.Enabled;

        public record PaymentInfo(string Id, string Status, bool Paid, string? ConfirmationUrl, string? OrderId, decimal Amount);

        public static string FormatAmount(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>
        /// Чек 54-ФЗ для ЮKassa: позиции заказа (блюда — товар, доставка — услуга).
        /// ЮKassa (или подключённая к ней облачная касса) пробьёт его и отправит гостю на телефон или почту.
        /// </summary>
        private object? Receipt(IReadOnlyList<Fiscal.ReceiptLine>? lines, string? customerPhone)
        {
            if (!_options.SendReceipt || lines == null || lines.Count == 0) return null;
            return new
            {
                Customer = new { Phone = NormalizePhone(customerPhone) },
                Items = lines.Select(l => new
                {
                    Description = l.Name.Length > 128 ? l.Name[..128] : l.Name,
                    Quantity = l.Quantity.ToString("0.000", CultureInfo.InvariantCulture),
                    Amount = new { Value = FormatAmount(l.Price), Currency = "RUB" },
                    VatCode = _options.VatCode,
                    PaymentMode = "full_payment",
                    PaymentSubject = l.IsService ? "service" : "commodity"
                }).ToArray()
            };
        }

        /// <summary>Создать платёж с переадресацией на страницу оплаты</summary>
        public async Task<PaymentInfo?> CreatePaymentAsync(
            int orderId, decimal amount, string description, string returnUrl, string? customerPhone,
            IReadOnlyList<Fiscal.ReceiptLine>? receiptLines, string idempotenceKey, CancellationToken ct = default)
        {
            var receipt = Receipt(receiptLines, customerPhone);

            var body = new
            {
                Amount = new { Value = FormatAmount(amount), Currency = "RUB" },
                Capture = true,
                Confirmation = new { Type = "redirect", ReturnUrl = returnUrl },
                Description = description,
                Metadata = new Dictionary<string, string> { ["order_id"] = orderId.ToString(CultureInfo.InvariantCulture) },
                Receipt = receipt
            };

            return await SendAsync(HttpMethod.Post, "payments", body, idempotenceKey, ct);
        }

        public Task<PaymentInfo?> GetPaymentAsync(string paymentId, CancellationToken ct = default) =>
            SendAsync(HttpMethod.Get, $"payments/{Uri.EscapeDataString(paymentId)}", null, null, ct);

        /// <summary>Вернуть деньги по платежу (полностью)</summary>
        public async Task<bool> RefundAsync(string paymentId, decimal amount, string idempotenceKey,
            IReadOnlyList<Fiscal.ReceiptLine>? receiptLines = null, string? customerPhone = null, CancellationToken ct = default)
        {
            var body = new
            {
                PaymentId = paymentId,
                Amount = new { Value = FormatAmount(amount), Currency = "RUB" },
                // С чеком ЮKassa пробьёт и отправит гостю чек возврата
                Receipt = Receipt(receiptLines, customerPhone)
            };
            using var request = BuildRequest(HttpMethod.Post, "refunds", body, idempotenceKey);
            try
            {
                using var response = await _http.SendAsync(request, ct);
                if (response.IsSuccessStatusCode) return true;
                _logger.LogWarning("ЮKassa refund {PaymentId} → {Status}: {Body}",
                    paymentId, (int)response.StatusCode, await response.Content.ReadAsStringAsync(ct));
                return false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "ЮKassa refund {PaymentId} не удался", paymentId);
                return false;
            }
        }

        private HttpRequestMessage BuildRequest(HttpMethod method, string path, object? body, string? idempotenceKey)
        {
            var request = new HttpRequestMessage(method, BaseUrl + path);
            if (body != null) request.Content = JsonContent.Create(body, options: JsonOptions);
            if (idempotenceKey != null) request.Headers.Add("Idempotence-Key", idempotenceKey);
            return request;
        }

        private async Task<PaymentInfo?> SendAsync(HttpMethod method, string path, object? body, string? idempotenceKey, CancellationToken ct)
        {
            if (!Enabled) return null;
            using var request = BuildRequest(method, path, body, idempotenceKey);
            try
            {
                using var response = await _http.SendAsync(request, ct);
                var json = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("ЮKassa {Method} {Path} → {Status}: {Body}", method, path, (int)response.StatusCode, json);
                    return null;
                }
                return ParsePayment(json);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "ЮKassa {Method} {Path} не удался", method, path);
                return null;
            }
        }

        private static PaymentInfo? ParsePayment(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var id)) return null;

            string? confirmationUrl = null;
            if (root.TryGetProperty("confirmation", out var conf) && conf.TryGetProperty("confirmation_url", out var url))
                confirmationUrl = url.GetString();

            string? orderId = null;
            if (root.TryGetProperty("metadata", out var meta) && meta.TryGetProperty("order_id", out var oid))
                orderId = oid.GetString();

            decimal amount = 0;
            if (root.TryGetProperty("amount", out var amt) && amt.TryGetProperty("value", out var val))
                decimal.TryParse(val.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out amount);

            return new PaymentInfo(
                id.GetString() ?? string.Empty,
                root.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
                root.TryGetProperty("paid", out var paid) && paid.ValueKind == JsonValueKind.True,
                confirmationUrl,
                orderId,
                amount);
        }

        private static string? NormalizePhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return null;
            var digits = new string(phone.Where(char.IsDigit).ToArray());
            if (digits.Length == 11 && digits.StartsWith('8')) digits = "7" + digits[1..];
            return digits.Length >= 10 ? digits : null;
        }
    }
}
