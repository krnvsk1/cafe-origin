using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Kumashaurma.API.Services.Egais
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
    }

    public record UtmDocumentRef(string Url, string Type, string? ReplyId);

    /// <summary>
    /// HTTP-интерфейс УТМ: /opt/out — входящие документы, /opt/in/{тип} — отправка (multipart, поле xml_file).
    /// </summary>
    public class UtmClient
    {
        private readonly HttpClient _http;
        private readonly EgaisOptions _options;

        public UtmClient(HttpClient http, IOptions<EgaisOptions> options)
        {
            _http = http;
            _options = options.Value;
        }

        private Uri Base => new(_options.UtmUrl.TrimEnd('/') + "/");

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

        public async Task<bool> PingAsync(CancellationToken ct = default)
        {
            try
            {
                using var res = await _http.GetAsync(new Uri(Base, "opt/out?limit=1"), ct);
                return res.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
