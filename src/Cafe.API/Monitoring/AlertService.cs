using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Cafe.API.Services;

namespace Cafe.API.Monitoring
{
    /// <summary>
    /// Тревоги в Telegram: ошибки сервера (одинаковые — не чаще раза в AlertThrottleMinutes)
    /// и смена состояния системы (база недоступна, сбои фоновых задач, мало места) — раз в минуту проверяем.
    /// </summary>
    public class AlertService : BackgroundService
    {
        private readonly RecentLogSink _sink;
        private readonly IServiceProvider _services;
        private readonly HealthCheckService _health;
        private readonly MonitoringOptions _options;
        private readonly AlertSettingsProvider _settings;
        private readonly ILogger<AlertService> _logger;
        private readonly Dictionary<string, DateTime> _lastSent = new();
        private readonly Dictionary<string, int> _suppressed = new();

        public AlertService(RecentLogSink sink, IServiceProvider services, HealthCheckService health,
            IOptions<MonitoringOptions> options, AlertSettingsProvider settings, ILogger<AlertService> logger)
        {
            _settings = settings;
            _sink = sink;
            _services = services;
            _health = health;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Чат могут задать позже в «Состоянии системы» — следим всегда, а шлём, только если он задан
            await Task.WhenAll(ErrorsAsync(stoppingToken), HealthAsync(stoppingToken));
        }

        private async Task ErrorsAsync(CancellationToken ct)
        {
            try
            {
                await foreach (var e in _sink.Alerts.Reader.ReadAllAsync(ct))
                {
                    // Свои же сбои отправки не пересылаем — иначе петля
                    if (e.Source?.Contains(nameof(AlertService)) == true || e.Source?.Contains(nameof(TelegramApi)) == true) continue;
                    var key = $"{e.Source}|{e.Template}";
                    var throttle = TimeSpan.FromMinutes(Math.Max(1, _options.AlertThrottleMinutes));
                    if (_lastSent.TryGetValue(key, out var last) && DateTime.UtcNow - last < throttle)
                    {
                        _suppressed[key] = _suppressed.GetValueOrDefault(key) + 1;
                        continue;
                    }
                    var repeats = _suppressed.GetValueOrDefault(key);
                    _suppressed.Remove(key);
                    _lastSent[key] = DateTime.UtcNow;

                    var text = $"🔴 <b>Ошибка на сервере</b>\n{Html(e.Message)}" +
                               (e.Source != null ? $"\n<i>{Html(Short(e.Source))}</i>" : "") +
                               (e.RequestId != null ? $"\nзапрос {Html(e.RequestId)}" : "") +
                               (repeats > 0 ? $"\nи ещё {repeats} таких же за последние {_options.AlertThrottleMinutes} мин" : "") +
                               (e.Exception != null ? $"\n<pre>{Html(FirstLines(e.Exception, 6))}</pre>" : "");
                    await SendAsync(text, ct);
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task HealthAsync(CancellationToken ct)
        {
            var known = new Dictionary<string, HealthStatus>();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var report = await _health.CheckHealthAsync(ct);
                        foreach (var (name, entry) in report.Entries)
                        {
                            var was = known.GetValueOrDefault(name, HealthStatus.Healthy);
                            known[name] = entry.Status;
                            if (was == entry.Status) continue;
                            var text = entry.Status == HealthStatus.Healthy
                                ? $"🟢 <b>Снова в порядке:</b> {Html(Title(name))}"
                                : $"{(entry.Status == HealthStatus.Unhealthy ? "🔴" : "🟡")} <b>{Html(Title(name))}</b>: {Html(entry.Description ?? entry.Exception?.Message ?? entry.Status.ToString())}";
                            await SendAsync(text, ct);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Проверка состояния системы не удалась");
                    }
                    await Task.Delay(TimeSpan.FromMinutes(1), ct);
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task SendAsync(string html, CancellationToken ct)
        {
            try
            {
                var chatId = await _settings.ChatIdAsync();
                if (chatId == null) return;
                var telegram = _services.GetRequiredService<TelegramApi>();
                if (!telegram.Enabled) return;
                if (html.Length > 3800) html = html[..3800] + "…";
                await telegram.SendMessageAsync(chatId.Value, html, null, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Тревога в Telegram не отправлена: {Message}", ex.Message);
            }
        }

        public static string Title(string check) => check switch
        {
            "database" => "База данных",
            "disk" => "Место на диске",
            "jobs" => "Фоновые задачи",
            "endpoints" => "Ручки API",
            _ => check
        };

        private static string Html(string s) => WebUtility.HtmlEncode(s);
        private static string Short(string source) => source.Split('.').LastOrDefault() ?? source;
        private static string FirstLines(string s, int n) => string.Join('\n', s.Split('\n').Take(n));
    }
}
