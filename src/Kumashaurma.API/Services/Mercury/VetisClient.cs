using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Services.Mercury
{
    public class MercuryOptions
    {
        public bool Enabled { get; set; }
        /// <summary>Тестовый контур api2.vetrf.ru</summary>
        public bool Sandbox { get; set; }
        /// <summary>API-ключ из письма о доступе к Ветис.API</summary>
        public string ApiKey { get; set; } = string.Empty;
        /// <summary>Логин и пароль базовой аутентификации из того же письма</summary>
        public string Login { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        /// <summary>issuerId — идентификатор хозяйствующего субъекта в Ветис.API</summary>
        public string IssuerId { get; set; } = string.Empty;
        /// <summary>Логин пользователя ВетИС (Меркурий), от имени которого гасятся ВСД</summary>
        public string InitiatorLogin { get; set; } = string.Empty;
        /// <summary>GUID площадки (предприятия) заведения в Меркурии</summary>
        public string EnterpriseGuid { get; set; } = string.Empty;
        public int AutoSyncMinutes { get; set; } = 30;
        /// <summary>За сколько дней забрать ВСД при первой загрузке (не больше 31 — ограничение Меркурия на интервал)</summary>
        public int InitialDays { get; set; } = 14;

        public DateTime? LastSyncAt { get; set; }
        public string? LastError { get; set; }
        /// <summary>С какой даты оформления запрашивать при следующей загрузке</summary>
        public DateTime? SyncFrom { get; set; }
    }

    /// <summary>Настройки Меркурия в app_settings (ключ mercury)</summary>
    public class MercurySettingsProvider
    {
        public const string Key = "mercury";
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private readonly IServiceScopeFactory _scopes;
        private MercuryOptions? _current;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public MercurySettingsProvider(IServiceScopeFactory scopes) => _scopes = scopes;

        public async Task<MercuryOptions> GetAsync()
        {
            if (_current != null) return _current;
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key);
            _current = row == null ? new MercuryOptions() : JsonSerializer.Deserialize<MercuryOptions>(row.Value, Json) ?? new MercuryOptions();
            return _current;
        }

        public async Task SaveAsync(MercuryOptions options, int? userId)
        {
            await _lock.WaitAsync();
            try
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
            finally { _lock.Release(); }
        }
    }

    public class VetisException : Exception
    {
        public VetisException(string message) : base(message) { }
    }

    /// <summary>
    /// Ветис.API (Меркурий, mercury-g2b.service:3.0): SOAP, псевдо-асинхронно —
    /// подаём заявку (submitApplicationRequest), затем забираем результат (receiveApplicationResult).
    /// </summary>
    public class VetisClient
    {
        public const string ProdUrl = "https://api.vetrf.ru/platform/services/3.0/ApplicationManagementService";
        public const string SandboxUrl = "https://api2.vetrf.ru:8002/platform/services/3.0/ApplicationManagementService";
        public const string ServiceId = "mercury-g2b.service:3.0";

        public static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
        public static readonly XNamespace Ws = "http://api.vetrf.ru/schema/cdm/application/ws-definitions";
        public static readonly XNamespace App = "http://api.vetrf.ru/schema/cdm/application";
        public static readonly XNamespace Merc = "http://api.vetrf.ru/schema/cdm/mercury/g2b/applications/v3";
        public static readonly XNamespace Vd = "http://api.vetrf.ru/schema/cdm/mercury/vet-document/v2";
        public static readonly XNamespace Dt = "http://api.vetrf.ru/schema/cdm/dictionary/v2";
        public static readonly XNamespace Bs = "http://api.vetrf.ru/schema/cdm/base";

        private readonly HttpClient _http;

        public VetisClient(HttpClient http) => _http = http;

        /// <summary>Подать заявку и дождаться результата (обычно несколько секунд)</summary>
        public async Task<XElement> CallAsync(MercuryOptions o, XElement request, CancellationToken ct = default)
        {
            Validate(o);
            var appId = await SubmitAsync(o, request, ct);
            for (var attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(attempt < 3 ? 1000 : 2000, ct);
                var app = await ReceiveAsync(o, appId, ct);
                var status = app.Element(App + "status")?.Value;
                if (status is "IN_PROCESS" or "ACCEPTED") continue;
                if (status == "COMPLETED")
                    return app.Element(App + "result")?.Elements().FirstOrDefault()
                           ?? throw new VetisException("Меркурий вернул пустой результат");
                throw new VetisException($"Меркурий отклонил заявку: {Errors(app) ?? status ?? "без описания"}");
            }
            throw new VetisException($"Меркурий не ответил вовремя (заявка {appId}) — повторите позже");
        }

        public static void Validate(MercuryOptions o)
        {
            if (string.IsNullOrWhiteSpace(o.ApiKey) || string.IsNullOrWhiteSpace(o.Login) || string.IsNullOrWhiteSpace(o.Password) || string.IsNullOrWhiteSpace(o.IssuerId))
                throw new VetisException("Укажите API-ключ, логин, пароль и issuerId из письма о доступе к Ветис.API");
            if (string.IsNullOrWhiteSpace(o.InitiatorLogin)) throw new VetisException("Укажите логин пользователя ВетИС");
            if (string.IsNullOrWhiteSpace(o.EnterpriseGuid)) throw new VetisException("Укажите GUID площадки заведения в Меркурии");
        }

        private async Task<string> SubmitAsync(MercuryOptions o, XElement request, CancellationToken ct)
        {
            var body = new XElement(Ws + "submitApplicationRequest",
                new XElement(Ws + "apiKey", o.ApiKey.Trim()),
                new XElement(App + "application",
                    new XElement(App + "serviceId", ServiceId),
                    new XElement(App + "issuerId", o.IssuerId.Trim()),
                    new XElement(App + "issueDate", Moscow(DateTime.UtcNow)),
                    new XElement(App + "data", request)));
            var resp = await PostAsync(o, "submitApplicationRequest", body, ct);
            var app = resp.Descendants(App + "application").FirstOrDefault() ?? throw new VetisException("Меркурий не принял заявку");
            var id = app.Element(App + "applicationId")?.Value;
            if (string.IsNullOrEmpty(id)) throw new VetisException($"Меркурий не принял заявку: {Errors(app) ?? "без описания"}");
            return id;
        }

        private async Task<XElement> ReceiveAsync(MercuryOptions o, string applicationId, CancellationToken ct)
        {
            var body = new XElement(Ws + "receiveApplicationResultRequest",
                new XElement(Ws + "apiKey", o.ApiKey.Trim()),
                new XElement(Ws + "issuerId", o.IssuerId.Trim()),
                new XElement(Ws + "applicationId", applicationId));
            var resp = await PostAsync(o, "receiveApplicationResult", body, ct);
            return resp.Descendants(App + "application").FirstOrDefault() ?? throw new VetisException("Пустой ответ Меркурия на запрос результата");
        }

        private async Task<XElement> PostAsync(MercuryOptions o, string action, XElement body, CancellationToken ct)
        {
            var envelope = new XDocument(new XElement(Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soapenv", Soap),
                new XAttribute(XNamespace.Xmlns + "ws", Ws),
                new XAttribute(XNamespace.Xmlns + "app", App),
                new XAttribute(XNamespace.Xmlns + "merc", Merc),
                new XAttribute(XNamespace.Xmlns + "vd", Vd),
                new XAttribute(XNamespace.Xmlns + "dt", Dt),
                new XAttribute(XNamespace.Xmlns + "bs", Bs),
                new XElement(Soap + "Header"),
                new XElement(Soap + "Body", body)));

            using var req = new HttpRequestMessage(HttpMethod.Post, o.Sandbox ? SandboxUrl : ProdUrl)
            {
                Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml")
            };
            req.Headers.Add("SOAPAction", action);
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{o.Login.Trim()}:{o.Password}")));

            HttpResponseMessage res;
            try { res = await _http.SendAsync(req, ct); }
            catch (HttpRequestException ex) { throw new VetisException($"Нет связи с Меркурием: {ex.Message}"); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new VetisException("Меркурий не ответил вовремя"); }

            using (res)
            {
                var text = await res.Content.ReadAsStringAsync(ct);
                if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new VetisException("Меркурий не пустил: проверьте логин и пароль базовой аутентификации и API-ключ");
                XDocument doc;
                try { doc = XDocument.Parse(text); }
                catch (System.Xml.XmlException) { throw new VetisException($"Меркурий ответил HTTP {(int)res.StatusCode}: {Short(text)}"); }

                var fault = doc.Descendants(Soap + "Fault").FirstOrDefault();
                if (fault != null)
                {
                    var msg = fault.Element("faultstring")?.Value ?? fault.Value;
                    var details = string.Join("; ", fault.Descendants().Where(e => e.Name.LocalName == "error").Select(e => e.Value.Trim()).Where(v => v.Length > 0));
                    throw new VetisException($"Меркурий: {Short(details.Length > 0 ? details : msg)}");
                }
                return doc.Descendants(Soap + "Body").FirstOrDefault()?.Elements().FirstOrDefault()
                       ?? throw new VetisException($"Пустой ответ Меркурия (HTTP {(int)res.StatusCode})");
            }
        }

        private static string? Errors(XElement app)
        {
            var list = app.Descendants().Where(e => e.Name.LocalName == "error")
                .Select(e => (e.Attribute("code")?.Value is { Length: > 0 } c ? $"{c}: " : "") + e.Value.Trim())
                .Where(s => s.Trim().Length > 0).ToList();
            return list.Count > 0 ? string.Join("; ", list) : null;
        }

        private static string Short(string s)
        {
            s = s.Trim();
            return s.Length > 500 ? s[..500] + "…" : s;
        }

        /// <summary>Меркурий ждёт время по Москве без зоны: 2026-10-28T12:30:00</summary>
        public static string Moscow(DateTime utc) => utc.AddHours(3).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
    }
}
