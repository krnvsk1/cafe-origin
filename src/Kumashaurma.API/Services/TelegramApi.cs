using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Kumashaurma.API.Options;

namespace Kumashaurma.API.Services
{
    /// <summary>
    /// Минимальный клиент Telegram Bot API (без сторонних пакетов)
    /// </summary>
    public class TelegramApi
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly HttpClient _http;
        private readonly TelegramOptions _options;
        private readonly ILogger<TelegramApi> _logger;

        public TelegramApi(HttpClient http, IOptions<TelegramOptions> options, ILogger<TelegramApi> logger)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;
        }

        public bool Enabled => _options.Enabled;

        /// <summary>Вызов метода Bot API. Ошибки логируются, исключения не пробрасываются.</summary>
        public async Task<bool> CallAsync(string method, object payload, CancellationToken ct = default)
        {
            if (!Enabled) return false;
            try
            {
                var url = $"https://api.telegram.org/bot{_options.BotToken}/{method}";
                using var response = await _http.PostAsJsonAsync(url, payload, JsonOptions, ct);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("Telegram {Method} → {Status}: {Body}", method, (int)response.StatusCode, body);
                    return false;
                }
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Telegram {Method} не удался", method);
                return false;
            }
        }

        public Task<bool> SendMessageAsync(long chatId, string html, object? replyMarkup = null, CancellationToken ct = default) =>
            CallAsync("sendMessage", new
            {
                ChatId = chatId,
                Text = html,
                ParseMode = "HTML",
                DisableWebPagePreview = true,
                ReplyMarkup = replyMarkup
            }, ct);

        /// <summary>Кнопка, открывающая сайт как мини-приложение Telegram</summary>
        public object? WebAppKeyboard(string text, string? path = null)
        {
            if (string.IsNullOrWhiteSpace(_options.WebAppUrl)) return null;
            var url = _options.WebAppUrl.TrimEnd('/') + (path ?? "/");
            return new { InlineKeyboard = new[] { new[] { new { Text = text, WebApp = new { Url = url } } } } };
        }

        public static string Escape(string? text) =>
            System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
    }
}
