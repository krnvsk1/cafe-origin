using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Services.Chz
{
    public class ChzOptions
    {
        public bool Enabled { get; set; }
        /// <summary>Песочница (тестовый контур) вместо боевого</summary>
        public bool Sandbox { get; set; }
        /// <summary>ИНН организации — тот же, что в сертификате КЭП</summary>
        public string Inn { get; set; } = string.Empty;
        /// <summary>Товарные группы, с которыми работает заведение</summary>
        public List<string> ProductGroups { get; set; } = new() { "milk", "water", "beer" };
    }

    /// <summary>Настройки Честного знака из админки (app_settings, ключ chz)</summary>
    public class ChzSettingsProvider
    {
        public const string Key = "chz";
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private readonly IServiceScopeFactory _scopes;
        private ChzOptions? _current;

        public ChzSettingsProvider(IServiceScopeFactory scopes) => _scopes = scopes;

        public async Task<ChzOptions> GetAsync()
        {
            if (_current != null) return _current;
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key);
            _current = row == null ? new ChzOptions() : JsonSerializer.Deserialize<ChzOptions>(row.Value, Json) ?? new ChzOptions();
            return _current;
        }

        public async Task SaveAsync(ChzOptions options, int? userId)
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
            row.UpdatedByUserId = userId;
            await db.SaveChangesAsync();
            _current = options;
        }
    }

    public class ChzApiException : Exception
    {
        public int? StatusCode { get; }
        public ChzApiException(string message, int? statusCode = null) : base(message) => StatusCode = statusCode;
    }

    /// <summary>
    /// True API Честного знака. Подпись (КЭП, ГОСТ) делается в браузере пользователя через КриптоПро;
    /// здесь — только HTTP-вызовы: ключ авторизации, вход, создание документа, статус.
    /// </summary>
    public class ChzClient
    {
        public const string ProdBase = "https://markirovka.crpt.ru/api";
        public const string SandboxBase = "https://markirovka.sandbox.crptech.ru/api";

        private readonly HttpClient _http;

        public ChzClient(HttpClient http) => _http = http;

        private static string Base(ChzOptions o) => o.Sandbox ? SandboxBase : ProdBase;

        public record AuthKey([property: JsonPropertyName("uuid")] string Uuid, [property: JsonPropertyName("data")] string Data);

        /// <summary>Шаг 1 входа: случайная строка, которую нужно подписать КЭП (присоединённая подпись)</summary>
        public async Task<AuthKey> GetAuthKeyAsync(ChzOptions o, CancellationToken ct = default)
        {
            using var res = await _http.GetAsync($"{Base(o)}/v3/true-api/auth/key", ct);
            await EnsureOk(res, ct);
            return await res.Content.ReadFromJsonAsync<AuthKey>(cancellationToken: ct)
                   ?? throw new ChzApiException("Пустой ответ на запрос ключа авторизации");
        }

        /// <summary>Шаг 2 входа: подписанная строка → токен (действует около 10 часов)</summary>
        public async Task<string> SignInAsync(ChzOptions o, string uuid, string signature, CancellationToken ct = default)
        {
            using var res = await _http.PostAsJsonAsync($"{Base(o)}/v3/true-api/auth/simpleSignIn", new { uuid, data = signature }, ct);
            await EnsureOk(res, ct);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("token", out var t) && t.GetString() is { Length: > 0 } token
                ? token
                : throw new ChzApiException("Честный знак не вернул токен");
        }

        /// <summary>Создать документ (LK_RECEIPT — вывод из оборота); возвращает идентификатор документа</summary>
        public async Task<string> CreateDocumentAsync(ChzOptions o, string token, string productGroup, string type,
            string productDocumentBase64, string signature, CancellationToken ct = default)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base(o)}/v3/true-api/lk/documents/create?pg={Uri.EscapeDataString(productGroup)}")
            {
                Content = JsonContent.Create(new
                {
                    document_format = "MANUAL",
                    product_document = productDocumentBase64,
                    type,
                    signature
                })
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var res = await _http.SendAsync(req, ct);
            await EnsureOk(res, ct);
            // Ответ — идентификатор документа (строка, иногда в кавычках)
            return (await res.Content.ReadAsStringAsync(ct)).Trim().Trim('"');
        }

        /// <summary>Статус документа: CHECKED_OK, CHECKED_NOT_OK, IN_PROGRESS и т.п. и текст ошибок</summary>
        public async Task<(string Status, string? Errors)> GetDocumentStatusAsync(ChzOptions o, string token, string productGroup, string id, CancellationToken ct = default)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{Base(o)}/v4/true-api/doc/{Uri.EscapeDataString(id)}/info?pg={Uri.EscapeDataString(productGroup)}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var res = await _http.SendAsync(req, ct);
            await EnsureOk(res, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            return ParseStatus(body);
        }

        public record BalanceCode(string Cis, string Gtin, DateTime? EmissionDate);

        /// <summary>
        /// Коды маркировки на балансе участника (статус INTRODUCED — «в обороте») по товарной группе.
        /// Постранично по 1000, сортировка по убыванию даты эмиссии; maxCodes — защита от бесконечной выгрузки.
        /// </summary>
        public async Task<(List<BalanceCode> Codes, bool Truncated)> SearchIntroducedAsync(ChzOptions o, string token, string productGroup,
            DateTime from, int maxCodes = 20000, CancellationToken ct = default)
        {
            var result = new List<BalanceCode>();
            string sgtin = "0";
            string? lastDate = null;
            while (true)
            {
                var pagination = new Dictionary<string, object> { ["perPage"] = 1000, ["sgtin"] = sgtin };
                if (lastDate != null) pagination["lastEmissionDate"] = lastDate;
                var body = new
                {
                    filter = new
                    {
                        emissionDatePeriod = new { from = from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"), to = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'") },
                        states = new[] { new { status = "INTRODUCED" } },
                        productGroups = new[] { productGroup }
                    },
                    pagination
                };
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base(o)}/v4/true-api/cises/search") { Content = JsonContent.Create(body) };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var res = await _http.SendAsync(req, ct);
                await EnsureOk(res, ct);
                var (page, isLast) = ParseSearch(await res.Content.ReadAsStringAsync(ct));
                result.AddRange(page.Select(p => p.Code));
                if (isLast || page.Count == 0) return (result, false);
                if (result.Count >= maxCodes) return (result, true);
                sgtin = page[^1].Code.Cis;
                lastDate = page[^1].RawDate;
                if (lastDate == null) return (result, true);
            }
        }

        public static (List<(BalanceCode Code, string? RawDate)> Page, bool IsLast) ParseSearch(string body)
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var isLast = root.TryGetProperty("isLastPage", out var l) && l.ValueKind == JsonValueKind.True;
            var list = new List<(BalanceCode, string?)>();
            if (root.TryGetProperty("result", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var x in arr.EnumerateArray())
                {
                    string? Str(string k) => x.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                    var cis = Str("sgtin") ?? Str("cis");
                    if (string.IsNullOrEmpty(cis)) continue;
                    var raw = Str("emissionDate");
                    DateTime? date = DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal, out var d) ? d : null;
                    var gtin = Str("gtin") ?? (cis.Length >= 16 && cis.StartsWith("01") ? cis.Substring(2, 14) : "");
                    list.Add((new BalanceCode(cis, gtin, date), raw));
                }
            return (list, isLast);
        }

        public static (string Status, string? Errors) ParseStatus(string body)
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                ? doc.RootElement[0]
                : doc.RootElement;
            var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "UNKNOWN" : "UNKNOWN";

            string? errors = null;
            if (root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 0)
                errors = string.Join("; ", e.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString()).Take(20));
            else if (root.TryGetProperty("downloadDesc", out var dd) && dd.ValueKind == JsonValueKind.String)
                errors = dd.GetString();
            return (status, errors);
        }

        private static async Task EnsureOk(HttpResponseMessage res, CancellationToken ct)
        {
            if (res.IsSuccessStatusCode) return;
            var body = await res.Content.ReadAsStringAsync(ct);
            string? message = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                foreach (var key in new[] { "error_message", "errorMessage", "message", "description", "error" })
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                    {
                        message = v.GetString();
                        break;
                    }
            }
            catch (JsonException) { }
            message ??= body.Length > 300 ? body[..300] : body;
            throw new ChzApiException($"Честный знак ответил {(int)res.StatusCode}: {message}", (int)res.StatusCode);
        }
    }

    /// <summary>Работа с кодами маркировки</summary>
    public static class ChzCodes
    {
        private const char Gs = '\u001d';

        /// <summary>Длина серийного номера (после 21) по товарным группам — чтобы отрезать криптохвост, если сканер не передал разделитель GS</summary>
        private static readonly Dictionary<string, int> SerialLength = new()
        {
            ["milk"] = 6, ["water"] = 13, ["beer"] = 7, ["nabeer"] = 13, ["softdrinks"] = 13, ["lp"] = 13, ["shoes"] = 13,
        };

        /// <summary>
        /// Код из сканера → КИ без криптохвоста: (01)GTIN(21)серийный. Разделители GS, скобки и пробелы убираются.
        /// </summary>
        public static string Normalize(string raw, string productGroup)
        {
            var code = raw.Trim().Replace("(", "").Replace(")", "");
            // Некоторые сканеры вместо GS отдают «\u001d» текстом или символ ↔
            code = code.Replace("\\u001d", Gs.ToString()).Replace("\\x1d", Gs.ToString());
            var gs = code.IndexOf(Gs);
            if (gs > 0) return code[..gs];

            if (code.StartsWith("01") && code.Length > 18 && code.Substring(16, 2) == "21"
                && SerialLength.TryGetValue(productGroup, out var len) && code.Length > 18 + len)
                return code[..(18 + len)];

            return code;
        }

        public static List<string> Parse(string text, string productGroup) =>
            text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => Normalize(c, productGroup))
                .Where(c => c.Length >= 18)
                .Distinct()
                .ToList();
    }
}
