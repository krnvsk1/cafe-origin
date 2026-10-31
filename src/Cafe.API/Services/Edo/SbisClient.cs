using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Models;

namespace Cafe.API.Services.Edo
{
    public class SbisOptions
    {
        public bool Enabled { get; set; }
        public string Login { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        /// <summary>Номер аккаунта — если у логина несколько кабинетов</summary>
        public string? Account { get; set; }
        /// <summary>Как часто проверять новые накладные, минут (0 — только вручную)</summary>
        public int AutoSyncMinutes { get; set; } = 15;
        /// <summary>За сколько дней забрать накладные при первой синхронизации</summary>
        public int InitialDays { get; set; } = 14;

        // Курсор СБИС.СписокИзменений — с какого события продолжать
        public string? LastEventId { get; set; }
        public string? LastEventDate { get; set; }
        public DateTime? LastSyncAt { get; set; }
        public string? LastError { get; set; }
    }

    /// <summary>Настройки СБИС в app_settings (ключ sbis); держатся в памяти</summary>
    public class SbisSettingsProvider
    {
        public const string Key = "sbis";
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private readonly IServiceScopeFactory _scopes;
        private SbisOptions? _current;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public SbisSettingsProvider(IServiceScopeFactory scopes) => _scopes = scopes;

        public async Task<SbisOptions> GetAsync()
        {
            if (_current != null) return _current;
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key);
            _current = row == null ? new SbisOptions() : JsonSerializer.Deserialize<SbisOptions>(row.Value, Json) ?? new SbisOptions();
            return _current;
        }

        public async Task SaveAsync(SbisOptions options, int? userId)
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

    public class SbisException : Exception
    {
        public bool Unauthorized { get; }
        public SbisException(string message, bool unauthorized = false) : base(message) => Unauthorized = unauthorized;
    }

    /// <summary>
    /// API Saby (СБИС): JSON-RPC 2.0. Вход — СБИС.Аутентифицировать на /auth/service/,
    /// остальные команды — на /service/?srv=1 с заголовком X-SBISSessionID. Даты — по Москве.
    /// </summary>
    public class SbisClient
    {
        public const string AuthUrl = "https://online.sbis.ru/auth/service/";
        public const string ServiceUrl = "https://online.sbis.ru/service/?srv=1";
        public const string DateFormat = "dd.MM.yyyy HH.mm.ss";

        // Сессия живёт до 21 дня; держим одну на логин, при 401 входим заново
        private static readonly Dictionary<string, string> Sessions = new();
        private static readonly object SessionsLock = new();

        private readonly HttpClient _http;

        public SbisClient(HttpClient http)
        {
            _http = http;
            if (!_http.DefaultRequestHeaders.UserAgent.Any())
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("Cafe/1.0");
        }

        public record ChangesPage(List<SbisDocument> Documents, bool HasMore);

        public class SbisDocument
        {
            public string Id { get; set; } = string.Empty;
            public string? Direction { get; set; }
            public bool Deleted { get; set; }
            public string? Number { get; set; }
            public string? Date { get; set; }
            public string? CounterpartyInn { get; set; }
            public string? CounterpartyName { get; set; }
            public List<SbisEvent> Events { get; set; } = new();
        }

        public class SbisEvent
        {
            public string? Id { get; set; }
            public string? DateTime { get; set; }
            public string? Name { get; set; }
            public List<SbisAttachment> Attachments { get; set; } = new();
        }

        public class SbisAttachment
        {
            public string? Name { get; set; }
            public string? Type { get; set; }
            public string? FileName { get; set; }
            public string? FileUrl { get; set; }
            public bool Service { get; set; }
        }

        private static string SessionKey(SbisOptions o) => $"{o.Login}|{o.Account}";

        public async Task<string> AuthenticateAsync(SbisOptions o, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(o.Login) || string.IsNullOrWhiteSpace(o.Password))
                throw new SbisException("Укажите логин и пароль пользователя СБИС");

            var param = new JsonObject { ["Логин"] = o.Login.Trim(), ["Пароль"] = o.Password };
            if (!string.IsNullOrWhiteSpace(o.Account)) param["НомерАккаунта"] = o.Account.Trim();
            var result = await PostAsync(AuthUrl, "СБИС.Аутентифицировать", new JsonObject { ["Параметр"] = param }, null, ct);
            var sid = result?.GetValueKind() == JsonValueKind.String ? result.GetValue<string>() : null;
            if (string.IsNullOrEmpty(sid)) throw new SbisException("СБИС не вернул идентификатор сессии");
            lock (SessionsLock) Sessions[SessionKey(o)] = sid;
            return sid;
        }

        public void ForgetSession(SbisOptions o)
        {
            lock (SessionsLock) Sessions.Remove(SessionKey(o));
        }

        private async Task<string> SessionAsync(SbisOptions o, CancellationToken ct)
        {
            lock (SessionsLock)
                if (Sessions.TryGetValue(SessionKey(o), out var sid)) return sid;
            return await AuthenticateAsync(o, ct);
        }

        /// <summary>Вызов команды с повторным входом, если сессия истекла</summary>
        public async Task<JsonNode?> CallAsync(SbisOptions o, string method, JsonObject @params, CancellationToken ct = default)
        {
            var sid = await SessionAsync(o, ct);
            try
            {
                return await PostAsync(ServiceUrl, method, @params, sid, ct);
            }
            catch (SbisException ex) when (ex.Unauthorized)
            {
                ForgetSession(o);
                sid = await AuthenticateAsync(o, ct);
                return await PostAsync(ServiceUrl, method, (JsonObject)@params.DeepClone(), sid, ct);
            }
        }

        /// <summary>
        /// Изменения по документам с курсора (СБИС.СписокИзменений). Первый раз — с даты, дальше — с последнего события.
        /// </summary>
        public async Task<ChangesPage> ChangesAsync(SbisOptions o, string fromDate, string? eventId, CancellationToken ct = default)
        {
            var filter = new JsonObject
            {
                ["ДатаВремяС"] = fromDate,
                ["Навигация"] = new JsonObject { ["РазмерСтраницы"] = "50" }
            };
            if (!string.IsNullOrEmpty(eventId)) filter["ИдентификаторСобытия"] = eventId;
            var result = await CallAsync(o, "СБИС.СписокИзменений", new JsonObject { ["Фильтр"] = filter }, ct);
            return ParseChanges(result);
        }

        public static ChangesPage ParseChanges(JsonNode? result)
        {
            var docs = new List<SbisDocument>();
            if (result?["Документ"] is JsonArray arr)
                foreach (var d in arr.OfType<JsonObject>())
                {
                    var counterparty = d["Контрагент"]?["СвЮЛ"] ?? d["Контрагент"]?["СвФЛ"];
                    var doc = new SbisDocument
                    {
                        Id = Str(d["Идентификатор"]) ?? "",
                        Direction = Str(d["Направление"]),
                        Deleted = Str(d["Удален"]) is "Да" or "true",
                        Number = Str(d["Номер"]),
                        Date = Str(d["Дата"]),
                        CounterpartyInn = Str(counterparty?["ИНН"]),
                        CounterpartyName = Str(counterparty?["Название"]) ?? Str(counterparty?["ФИО"]),
                    };
                    if (d["Событие"] is JsonArray events)
                        foreach (var e in events.OfType<JsonObject>())
                        {
                            var ev = new SbisEvent { Id = Str(e["Идентификатор"]), DateTime = Str(e["ДатаВремя"]), Name = Str(e["Название"]) };
                            if (e["Вложение"] is JsonArray atts)
                                foreach (var a in atts.OfType<JsonObject>())
                                    ev.Attachments.Add(new SbisAttachment
                                    {
                                        Name = Str(a["Название"]),
                                        Type = Str(a["Тип"]),
                                        FileName = Str(a["Файл"]?["Имя"]),
                                        FileUrl = Str(a["Файл"]?["Ссылка"]),
                                        Service = Str(a["Служебный"]) is "Да" or "true",
                                    });
                            doc.Events.Add(ev);
                        }
                    if (doc.Id.Length > 0) docs.Add(doc);
                }
            var hasMore = Str(result?["Навигация"]?["ЕстьЕще"]) is "Да" or "true";
            return new ChangesPage(docs, hasMore);
        }

        /// <summary>Скачать вложение по ссылке из СписокИзменений (ссылка живёт месяц)</summary>
        public async Task<byte[]> DownloadAsync(SbisOptions o, string url, CancellationToken ct = default)
        {
            for (var attempt = 0; ; attempt++)
            {
                var sid = await SessionAsync(o, ct);
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("X-SBISSessionID", sid);
                using var res = await _http.SendAsync(req, ct);
                if (res.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    ForgetSession(o);
                    continue;
                }
                if (res.StatusCode == HttpStatusCode.Forbidden)
                    throw new SbisException("Ссылка на файл в СБИС устарела (действует месяц)");
                if (!res.IsSuccessStatusCode)
                    throw new SbisException($"СБИС не отдал файл: HTTP {(int)res.StatusCode}");
                return await res.Content.ReadAsByteArrayAsync(ct);
            }
        }

        private async Task<JsonNode?> PostAsync(string url, string method, JsonObject @params, string? sid, CancellationToken ct)
        {
            var body = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = @params, ["id"] = 0 };
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
            if (sid != null) req.Headers.Add("X-SBISSessionID", sid);
            using var res = await _http.SendAsync(req, ct);
            var text = await res.Content.ReadAsStringAsync(ct);

            JsonNode? json = null;
            try { json = JsonNode.Parse(text); } catch (JsonException) { }

            if (json?["error"] is JsonNode error)
            {
                var message = Str(error["message"]) ?? "ошибка";
                var details = Str(error["details"]);
                var full = details != null && details != message ? $"{message}: {details}" : message;
                var unauthorized = res.StatusCode == HttpStatusCode.Unauthorized
                                   || full.Contains("авторизац", StringComparison.OrdinalIgnoreCase) && sid != null;
                throw new SbisException($"СБИС: {full}", unauthorized);
            }
            if (res.StatusCode == HttpStatusCode.Unauthorized)
                throw new SbisException(sid == null ? "СБИС: неверный логин или пароль" : "СБИС: сессия истекла", unauthorized: true);
            if (!res.IsSuccessStatusCode || json == null)
                throw new SbisException($"СБИС ответил HTTP {(int)res.StatusCode}: {(text.Length > 300 ? text[..300] : text)}");
            return json["result"];
        }

        private static string? Str(JsonNode? n) =>
            n is JsonValue v ? (v.TryGetValue<string>(out var s) ? s : v.ToString()) : null;

        /// <summary>«25.10.2026 12.30.00» (Москва) → UTC</summary>
        public static DateTime? ParseDateTime(string? s) =>
            DateTime.TryParseExact(s, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? DateTime.SpecifyKind(d.AddHours(-3), DateTimeKind.Utc)
                : null;

        public static string FormatMoscow(DateTime utc) => utc.AddHours(3).ToString(DateFormat, CultureInfo.InvariantCulture);
    }
}
