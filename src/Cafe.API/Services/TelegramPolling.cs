using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Cafe.API.Controllers;
using Cafe.API.Options;

namespace Cafe.API.Services
{
    /// <summary>
    /// Сервер без публичного адреса (например, на ноутбуке): Telegram не может прислать сообщения через вебхук,
    /// поэтому забираем их сами (getUpdates) и обрабатываем так же, как вебхук: /start, /menu, /orders, /id, /status.
    /// Включается, только когда Telegram:PublicApiUrl не задан. Если у бота уже стоит вебхук — не трогаем его и выходим.
    /// </summary>
    public class TelegramPollingService : BackgroundService
    {
        private readonly TelegramOptions _options;
        private readonly IServiceScopeFactory _scopes;
        private readonly IHttpClientFactory _http;
        private readonly ILogger<TelegramPollingService> _logger;

        public TelegramPollingService(IOptions<TelegramOptions> options, IServiceScopeFactory scopes, IHttpClientFactory http,
            ILogger<TelegramPollingService> logger)
        {
            _options = options.Value;
            _scopes = scopes;
            _http = http;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled || !string.IsNullOrWhiteSpace(_options.PublicApiUrl)) return;

            using var client = _http.CreateClient("telegram-polling");
            client.Timeout = TimeSpan.FromSeconds(40);
            var baseUrl = $"https://api.telegram.org/bot{_options.BotToken}";
            long offset = 0;
            var failures = 0;
            _logger.LogInformation("Telegram: сервер без публичного адреса — получаем сообщения бота опросом");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var response = await client.GetAsync($"{baseUrl}/getUpdates?timeout=25&offset={offset}&allowed_updates=%5B%22message%22%5D", stoppingToken);
                    if (response.StatusCode == HttpStatusCode.Conflict)
                    {
                        _logger.LogWarning("Telegram: у бота настроен вебхук на другой сервер — опрос не включаем");
                        return;
                    }
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        _logger.LogWarning("Telegram: неверный токен бота — опрос не включаем");
                        return;
                    }
                    response.EnsureSuccessStatusCode();
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(stoppingToken));
                    failures = 0;
                    foreach (var update in doc.RootElement.GetProperty("result").EnumerateArray())
                    {
                        offset = Math.Max(offset, update.GetProperty("update_id").GetInt64() + 1);
                        using var scope = _scopes.CreateScope();
                        var controller = ActivatorUtilities.CreateInstance<TelegramController>(scope.ServiceProvider);
                        await controller.ProcessUpdateAsync(update.Clone());
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    // Нет интернета и т.п. — пробуем снова, всё реже (до минуты); в журнал — не на каждой попытке
                    failures++;
                    if (failures == 1 || failures % 10 == 0)
                        _logger.LogWarning("Telegram: опрос сообщений не удался ({Failures} раз подряд) — {Message}", failures, ex.Message);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * failures)), stoppingToken);
                }
            }
        }
    }
}
