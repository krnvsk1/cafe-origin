using System.Text;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Cafe.API.Monitoring
{
    /// <summary>
    /// Тревоги через внешний мониторинг (Healthchecks.io и похожие сервисы «пульса»):
    /// раз в минуту сервер сообщает «жив, всё в порядке» GET-запросом на адрес пинга, а при проблеме —
    /// POST на адрес/fail с описанием (база недоступна, сбои фоновых задач, мало места, ошибки сервера).
    /// Если сервер упал совсем, пинги перестают приходить — тревогу поднимет сам внешний сервис
    /// и пришлёт её на почту, SMS, в мессенджер — как настроено там.
    /// </summary>
    public class AlertService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

        private readonly RecentLogSink _sink;
        private readonly HealthCheckService _health;
        private readonly MonitoringOptions _options;
        private readonly AlertSettingsProvider _settings;
        private readonly IHttpClientFactory _http;
        private readonly ILogger<AlertService> _logger;
        private readonly Dictionary<string, DateTime> _lastSent = new();
        private readonly object _gate = new();
        // Ошибки сервера с прошлого пинга — уйдут с ближайшим /fail
        private readonly List<string> _errors = new();

        public AlertService(RecentLogSink sink, HealthCheckService health, IOptions<MonitoringOptions> options,
            AlertSettingsProvider settings, IHttpClientFactory http, ILogger<AlertService> logger)
        {
            _sink = sink;
            _health = health;
            _options = options.Value;
            _settings = settings;
            _http = http;
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
            Task.WhenAll(CollectErrorsAsync(stoppingToken), PulseAsync(stoppingToken));

        private async Task CollectErrorsAsync(CancellationToken ct)
        {
            try
            {
                await foreach (var e in _sink.Alerts.Reader.ReadAllAsync(ct))
                {
                    // Свои же сбои отправки не пересылаем — иначе петля
                    if (e.Source?.Contains(nameof(AlertService)) == true) continue;
                    var key = $"{e.Source}|{e.Template}";
                    var throttle = TimeSpan.FromMinutes(Math.Max(1, _options.AlertThrottleMinutes));
                    lock (_gate)
                    {
                        if (_lastSent.TryGetValue(key, out var last) && DateTime.UtcNow - last < throttle) continue;
                        _lastSent[key] = DateTime.UtcNow;
                        if (_errors.Count < 20)
                            _errors.Add($"Ошибка: {e.Message}" + (e.Source != null ? $" ({e.Source.Split('.').Last()})" : "") +
                                        (e.RequestId != null ? $", запрос {e.RequestId}" : ""));
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task PulseAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                while (!ct.IsCancellationRequested)
                {
                    try { await PulseOnceAsync(ct); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning("Пинг внешнего мониторинга не удался: {Message}", ex.Message);
                    }
                    await Task.Delay(Interval, ct);
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task PulseOnceAsync(CancellationToken ct)
        {
            var url = await _settings.PingUrlAsync();
            var problems = await ProblemsAsync(ct);
            List<string> errors;
            lock (_gate) { errors = _errors.ToList(); _errors.Clear(); }
            if (string.IsNullOrWhiteSpace(url)) return;

            problems.AddRange(errors);
            if (problems.Count == 0) await PingAsync(url, null, ct);
            else await PingAsync(url, string.Join('\n', problems), ct);
        }

        /// <summary>Что сейчас не в порядке по проверкам состояния (база, диск, фоновые задачи, ручки API)</summary>
        public async Task<List<string>> ProblemsAsync(CancellationToken ct)
        {
            var report = await _health.CheckHealthAsync(ct);
            return report.Entries
                .Where(e => e.Value.Status != HealthStatus.Healthy)
                .Select(e => $"{Title(e.Key)}: {e.Value.Description ?? e.Value.Exception?.Message ?? e.Value.Status.ToString()}")
                .ToList();
        }

        /// <summary>
        /// Пинг: problem == null — «всё в порядке» (GET адрес), иначе «проблема» (POST адрес/fail с текстом).
        /// Формат Healthchecks.io; другие сервисы обычно принимают хотя бы обычный GET.
        /// </summary>
        public async Task<bool> PingAsync(string url, string? problem, CancellationToken ct)
        {
            var client = _http.CreateClient(nameof(AlertService));
            var baseUrl = url.Trim().TrimEnd('/');
            using var res = problem == null
                ? await client.GetAsync(baseUrl, ct)
                : await client.PostAsync(baseUrl + "/fail", new StringContent(Trunc(problem, 9000), Encoding.UTF8, "text/plain"), ct);
            if (!res.IsSuccessStatusCode)
                _logger.LogWarning("Внешний мониторинг ответил HTTP {Status}", (int)res.StatusCode);
            return res.IsSuccessStatusCode;
        }

        public static string Title(string check) => check switch
        {
            "database" => "База данных",
            "disk" => "Место на диске",
            "jobs" => "Фоновые задачи",
            "endpoints" => "Ручки API",
            _ => check
        };

        private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
    }
}
