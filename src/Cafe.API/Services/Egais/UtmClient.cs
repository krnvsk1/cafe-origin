using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace Cafe.API.Services.Egais
{
    public class EgaisOptions
    {
        public const string SectionName = "Egais";

        /// <summary>Включить интеграцию</summary>
        public bool Enabled { get; set; }

        /// <summary>Адрес УТМ. По умолчанию УТМ слушает http://localhost:8080 на компьютере с ключом</summary>
        public string UtmUrl { get; set; } = "http://localhost:8080";

        /// <summary>ФСРАР ID заведения (12 цифр, указан в УТМ и в личном кабинете ЕГАИС)</summary>
        public string FsrarId { get; set; } = string.Empty;

        /// <summary>Автоматически проверять входящие каждые N минут (0 — только вручную)</summary>
        public int AutoSyncMinutes { get; set; } = 10;

        /// <summary>
        /// Удалять обработанные документы из УТМ. Выключено по умолчанию: если с тем же УТМ работает iiko
        /// или другая программа, она тоже должна увидеть накладные.
        /// </summary>
        public bool DeleteProcessed { get; set; }

        /// <summary>
        /// Отправлять акты приёмки в ЕГАИС. Выключите, если акты отправляет iiko — тогда сервис только оприходует товар на склад.
        /// </summary>
        public bool SendActs { get; set; } = true;
    }

    public record UtmDocumentRef(string Url, string Type, string? ReplyId);

    /// <summary>
    /// HTTP-интерфейс УТМ: /opt/out — входящие документы, /opt/in/{тип} — отправка (multipart, поле xml_file).
    /// </summary>
    public class UtmClient
    {
        private readonly HttpClient _http;
        private readonly EgaisSettingsProvider _settings;

        public UtmClient(HttpClient http, EgaisSettingsProvider settings)
        {
            _http = http;
            _settings = settings;
        }

        private Uri Base => BaseOf(_settings.Current.UtmUrl);
        private static Uri BaseOf(string url) => new(url.Trim().TrimEnd('/') + "/");

        /// <summary>
        /// УТМ возвращает ссылки вида http://localhost:8080/opt/out/WayBill_v4/12 — берём путь
        /// и подставляем настроенный адрес (если УТМ доступен через туннель).
        /// </summary>
        private Uri Resolve(string url)
        {
            var path = Uri.TryCreate(url, UriKind.Absolute, out var abs) ? abs.PathAndQuery : url;
            return new Uri(Base, path.TrimStart('/'));
        }

        public async Task<List<UtmDocumentRef>> ListIncomingAsync(CancellationToken ct = default)
        {
            var xml = await _http.GetStringAsync(new Uri(Base, "opt/out"), ct);
            return ParseList(xml);
        }

        public static List<UtmDocumentRef> ParseList(string xml)
        {
            var doc = EgaisXml.Load(xml);
            return doc.Descendants()
                .Where(e => e.Name.LocalName == "url")
                .Select(e =>
                {
                    var url = e.Value.Trim();
                    // .../opt/out/{Тип}/{id}
                    var segments = (Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.AbsolutePath : url)
                        .Split('/', StringSplitOptions.RemoveEmptyEntries);
                    var type = segments.Length >= 2 ? segments[^2] : "";
                    return new UtmDocumentRef(url, type, e.Attribute("replyId")?.Value);
                })
                .Where(r => r.Url.Length > 0)
                .ToList();
        }

        public Task<string> GetAsync(string url, CancellationToken ct = default) => _http.GetStringAsync(Resolve(url), ct);

        public async Task DeleteAsync(string url, CancellationToken ct = default)
        {
            using var res = await _http.DeleteAsync(Resolve(url), ct);
            res.EnsureSuccessStatusCode();
        }

        /// <summary>Отправить документ; УТМ отвечает квитанцией приёма (replyId) или ошибкой проверки</summary>
        public async Task<(bool Ok, string Response)> SendAsync(string docType, string xml, CancellationToken ct = default)
        {
            using var content = new MultipartFormDataContent();
            var file = new ByteArrayContent(Encoding.UTF8.GetBytes(xml));
            file.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
            content.Add(file, "xml_file", $"{docType}.xml");

            using var res = await _http.PostAsync(new Uri(Base, $"opt/in/{docType}"), content, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            // Ошибку проверки УТМ возвращает в теле (<error>…</error>), иногда с кодом 200
            var hasError = !res.IsSuccessStatusCode || body.Contains("<error", StringComparison.OrdinalIgnoreCase);
            return (!hasError, body);
        }

        /// <summary>Транспортный идентификатор из ответа УТМ на отправку (по нему потом приходит квитанция)</summary>
        public static string? ExtractTransportId(string response)
        {
            try
            {
                var doc = EgaisXml.Load(response);
                return doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "url")?.Value.Trim() is { Length: > 0 } id ? id : null;
            }
            catch
            {
                return null;
            }
        }

        public static string? ExtractError(string response)
        {
            try
            {
                var doc = EgaisXml.Load(response);
                return doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "error")?.Value.Trim();
            }
            catch
            {
                return response.Length > 300 ? response[..300] : response;
            }
        }

        /// <summary>Сколько ждать УТМ при проверке подключения из настроек</summary>
        public static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(8);

        /// <summary>Отвечает ли УТМ (по сохранённому или переданному адресу)</summary>
        public async Task<(bool Ok, string? Error)> PingAsync(string? url = null, CancellationToken ct = default, TimeSpan? timeout = null)
        {
            var limit = timeout ?? PingTimeout;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(limit);
                using var res = await _http.GetAsync(new Uri(url != null ? BaseOf(url) : Base, "opt/out?limit=1"), cts.Token);
                if (!res.IsSuccessStatusCode) return (false, $"УТМ ответил кодом {(int)res.StatusCode}");
                var body = await res.Content.ReadAsStringAsync(cts.Token);
                return body.Contains("<", StringComparison.Ordinal) ? (true, null) : (false, "По адресу отвечает не УТМ");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return (false, $"УТМ не ответил за {limit.TotalSeconds:0} с");
            }
            catch (Exception ex) when (ex is HttpRequestException or UriFormatException)
            {
                var target = url != null ? BaseOf(url) : Base;
                var local = target.IsLoopback;
                return (false, local
                    ? $"Нет связи: {ex.Message}. «localhost» — это компьютер, где запущен сервер сервиса. " +
                      "Если УТМ стоит на другом компьютере, укажите его адрес в сети, например http://192.168.1.20:8080"
                    : $"Нет связи: {ex.Message}. Проверьте, что компьютер с УТМ включён, в той же сети и порт открыт в брандмауэре");
            }
        }
    }
}
