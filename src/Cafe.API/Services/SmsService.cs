using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Cafe.API.Options;

namespace Cafe.API.Services
{
    public interface ISmsService
    {
        /// <summary>Подключён ли SMS-сервис (иначе коды только пишутся в журнал — для разработки)</summary>
        bool Enabled { get; }
        Task<(bool Success, string? Message)> SendVerificationCodeAsync(string phone, string code);
    }

    /// <summary>SMS-коды через SMS Aero или SMS.ru; без настроек — код только в журнал (разработка)</summary>
    public class SmsService : ISmsService
    {
        private readonly SmsOptions _options;
        private readonly HttpClient _http;
        private readonly IHostEnvironment _env;
        private readonly ILogger<SmsService> _logger;

        public SmsService(IOptions<SmsOptions> options, HttpClient http, IHostEnvironment env, ILogger<SmsService> logger)
        {
            _options = options.Value;
            _http = http;
            _env = env;
            _logger = logger;
        }

        public bool Enabled => _options.Enabled;

        public async Task<(bool Success, string? Message)> SendVerificationCodeAsync(string phone, string code)
        {
            var text = $"Код подтверждения: {code}";
            if (!_options.Enabled)
            {
                if (!_env.IsDevelopment()) return (false, "Отправка SMS не настроена");
                _logger.LogInformation("SMS-код для {Phone}: {Code} (SMS-сервис не настроен)", phone, code);
                return (true, null);
            }

            var number = new string(phone.Where(char.IsDigit).ToArray());
            try
            {
                return _options.ProviderKey == "smsaero"
                    ? await SendSmsAeroAsync(number, text)
                    : await SendSmsRuAsync(number, text);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("SMS не отправлено на {Phone}: {Error}", Mask(number), ex.Message);
                return (false, "Не удалось отправить SMS — попробуйте ещё раз");
            }
        }

        // https://smsaero.ru/integration/documentation/api/ — GET /v2/sms/send, Basic email:apikey
        private async Task<(bool, string?)> SendSmsAeroAsync(string number, string text)
        {
            var sign = string.IsNullOrWhiteSpace(_options.Sender) ? "SMS Aero" : _options.Sender!.Trim();
            var url = $"https://gate.smsaero.ru/v2/sms/send?number={number}&text={Uri.EscapeDataString(text)}&sign={Uri.EscapeDataString(sign)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Login!.Trim()}:{_options.ApiKey!.Trim()}")));
            using var res = await _http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var ok = res.IsSuccessStatusCode && doc.RootElement.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
            if (ok) return (true, null);
            var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
            _logger.LogWarning("SMS Aero: не отправлено на {Phone}: {Error}", Mask(number), msg ?? $"HTTP {(int)res.StatusCode}");
            return (false, "Не удалось отправить SMS — попробуйте ещё раз");
        }

        // https://sms.ru/api/send — GET /sms/send?api_id&to&msg&json=1
        private async Task<(bool, string?)> SendSmsRuAsync(string number, string text)
        {
            var url = $"https://sms.ru/sms/send?api_id={Uri.EscapeDataString(_options.ApiKey!.Trim())}&to={number}&msg={Uri.EscapeDataString(text)}&json=1"
                      + (string.IsNullOrWhiteSpace(_options.Sender) ? "" : $"&from={Uri.EscapeDataString(_options.Sender!.Trim())}");
            using var res = await _http.GetAsync(url);
            var body = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("status", out var st) && st.GetString() == "OK";
            string? error = root.TryGetProperty("status_text", out var stt) ? stt.GetString() : null;
            if (ok && root.TryGetProperty("sms", out var sms) && sms.ValueKind == JsonValueKind.Object)
                foreach (var p in sms.EnumerateObject())
                    if (p.Value.TryGetProperty("status", out var ps) && ps.GetString() != "OK")
                    {
                        ok = false;
                        error = p.Value.TryGetProperty("status_text", out var pt) ? pt.GetString() : error;
                    }
            if (ok) return (true, null);
            _logger.LogWarning("SMS.ru: не отправлено на {Phone}: {Error}", Mask(number), error ?? $"HTTP {(int)res.StatusCode}");
            return (false, "Не удалось отправить SMS — попробуйте ещё раз");
        }

        private static string Mask(string number) => number.Length > 4 ? new string('*', number.Length - 4) + number[^4..] : number;
    }
}
