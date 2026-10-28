using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Services.Fiscal
{
    public class FiscalOptions
    {
        public bool Enabled { get; set; }
        /// <summary>Адрес АТОЛ Web Server (входит в драйвер ДТО 10), например http://192.168.1.86:16732</summary>
        public string Url { get; set; } = "http://127.0.0.1:16732";
        /// <summary>Система налогообложения: osn, usnIncome, usnIncomeOutcome, esn, patent</summary>
        public string TaxationType { get; set; } = "usnIncome";
        /// <summary>Ставка НДС позиций: none, vat0, vat5, vat7, vat10, vat20, vat22</summary>
        public string Vat { get; set; } = "none";
        /// <summary>Признак предмета расчёта для блюд: commodity (товар) или service (услуга)</summary>
        public string PaymentObject { get; set; } = "commodity";
        /// <summary>ИНН кассира (необязательно)</summary>
        public string? CashierInn { get; set; }
        /// <summary>Отправлять копию чека на телефон гостя, если он указан</summary>
        public bool SendToCustomer { get; set; } = true;
    }

    /// <summary>Настройки онлайн-кассы в app_settings (ключ fiscal)</summary>
    public class FiscalSettingsProvider
    {
        public const string Key = "fiscal";
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private readonly IServiceScopeFactory _scopes;
        private FiscalOptions? _current;

        public FiscalSettingsProvider(IServiceScopeFactory scopes) => _scopes = scopes;

        public async Task<FiscalOptions> GetAsync()
        {
            if (_current != null) return _current;
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key);
            _current = row == null ? new FiscalOptions() : JsonSerializer.Deserialize<FiscalOptions>(row.Value, Json) ?? new FiscalOptions();
            return _current;
        }

        public async Task SaveAsync(FiscalOptions options, int? userId)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == Key);
            if (row == null)
            {
                row = new AppSetting { Key = Key };
                db.AppSettings.Add(row);
            }
            row.Value = JsonSerializer.Serialize(options, Json);
            row.UpdatedAt = DateTime.UtcNow;
            if (userId != null) row.UpdatedByUserId = userId;
            await db.SaveChangesAsync();
            _current = options;
        }
    }

    public class FiscalException : Exception
    {
        public FiscalException(string message) : base(message) { }
    }

    /// <summary>Итог задания на кассе: Pending — ещё выполняется (или ждёт в очереди)</summary>
    public record AtolResult(bool Ready, bool Pending, string? Error, JsonNode? Result);

    /// <summary>
    /// АТОЛ Web Server (ДТО 10): задание в JSON отправляется POST /api/v2/requests {uuid, request: [...]},
    /// результат — GET /api/v2/requests/{uuid}. Повторная отправка того же uuid не пробьёт второй чек.
    /// </summary>
    public class AtolClient
    {
        private readonly HttpClient _http;

        public AtolClient(HttpClient http) => _http = http;

        public async Task SubmitAsync(FiscalOptions o, string uuid, JsonObject task, CancellationToken ct)
        {
            var body = new JsonObject { ["uuid"] = uuid, ["request"] = new JsonArray(task) };
            HttpResponseMessage res;
            try { res = await _http.PostAsJsonAsync(Base(o) + "/api/v2/requests", body, ct); }
            catch (HttpRequestException ex) { throw new FiscalException($"Касса недоступна ({Base(o)}): {ex.Message}. Проверьте, что включён АТОЛ Web Server"); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new FiscalException("Касса не ответила вовремя"); }
            using (res)
            {
                if (res.IsSuccessStatusCode) return;
                var text = await res.Content.ReadAsStringAsync(ct);
                // 409 — задание с таким uuid уже есть: значит, оно уже отправлено, результат заберём по uuid
                if ((int)res.StatusCode == 409) return;
                throw new FiscalException($"Касса не приняла задание (HTTP {(int)res.StatusCode}): {ErrorText(text)}");
            }
        }

        public async Task<AtolResult> ResultAsync(FiscalOptions o, string uuid, CancellationToken ct)
        {
            HttpResponseMessage res;
            try { res = await _http.GetAsync(Base(o) + "/api/v2/requests/" + Uri.EscapeDataString(uuid), ct); }
            catch (HttpRequestException ex) { throw new FiscalException($"Касса недоступна ({Base(o)}): {ex.Message}"); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new FiscalException("Касса не ответила вовремя"); }
            using (res)
            {
                var text = await res.Content.ReadAsStringAsync(ct);
                if ((int)res.StatusCode == 404) return new AtolResult(false, false, "Касса не знает такого задания", null);
                if (!res.IsSuccessStatusCode) throw new FiscalException($"Касса ответила HTTP {(int)res.StatusCode}: {ErrorText(text)}");
                return Parse(text);
            }
        }

        /// <summary>Отправить задание и дождаться результата (обычно 1–5 секунд, печать чека)</summary>
        public async Task<AtolResult> RunAsync(FiscalOptions o, string uuid, JsonObject task, CancellationToken ct, int waitSeconds = 30)
        {
            await SubmitAsync(o, uuid, task, ct);
            return await WaitAsync(o, uuid, ct, waitSeconds);
        }

        public async Task<AtolResult> WaitAsync(FiscalOptions o, string uuid, CancellationToken ct, int waitSeconds = 30)
        {
            var until = DateTime.UtcNow.AddSeconds(waitSeconds);
            AtolResult r;
            do
            {
                await Task.Delay(500, ct);
                r = await ResultAsync(o, uuid, ct);
                if (!r.Pending) return r;
            } while (DateTime.UtcNow < until);
            return r;
        }

        public static AtolResult Parse(string json)
        {
            JsonNode? root;
            try { root = JsonNode.Parse(json); }
            catch (JsonException) { return new AtolResult(false, false, $"Непонятный ответ кассы: {Short(json)}", null); }
            var first = (root?["results"] as JsonArray)?.FirstOrDefault();
            if (first == null) return new AtolResult(false, true, null, null);
            var status = first["status"]?.GetValue<string>();
            var errorCode = first["errorCode"] is JsonValue ec && ec.TryGetValue<int>(out var c) ? c : 0;
            var errorText = first["errorDescription"]?.GetValue<string>();
            return status switch
            {
                "ready" when errorCode == 0 => new AtolResult(true, false, null, first["result"]),
                "ready" => new AtolResult(false, false, Describe(errorCode, errorText), first["result"]),
                "wait" or "inProgress" => new AtolResult(false, true, null, null),
                "blocked" => new AtolResult(false, true, "Касса ждёт решения по предыдущему заданию (очередь заблокирована)", null),
                "canceled" => new AtolResult(false, false, "Задание отменено на кассе", null),
                _ => new AtolResult(false, false, Describe(errorCode, errorText ?? status), null),
            };
        }

        private static string Describe(int code, string? text) =>
            string.IsNullOrWhiteSpace(text) ? $"ошибка кассы {code}" : code != 0 ? $"{text} (код {code})" : text;

        private static string ErrorText(string text)
        {
            try
            {
                var n = JsonNode.Parse(text);
                var d = n?["error"]?["description"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(d)) return d;
            }
            catch (JsonException) { }
            return Short(text);
        }

        private static string Short(string s)
        {
            s = s.Trim();
            return s.Length > 300 ? s[..300] + "…" : s;
        }

        private static string Base(FiscalOptions o) => o.Url.Trim().TrimEnd('/');
    }
}
