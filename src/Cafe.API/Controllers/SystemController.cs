using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Cafe.API.Authorization;
using Cafe.API.Data;
using Cafe.API.Extensions;
using Cafe.API.Monitoring;

namespace Cafe.API.Controllers
{
    /// <summary>Состояние системы для администратора и журнал ошибок из браузера</summary>
    [ApiController]
    public class SystemController : ControllerBase
    {
        public static readonly DateTime StartedAt = DateTime.UtcNow;

        private readonly HealthCheckService _health;
        private readonly RecentLogSink _logs;
        private readonly JobMonitor _jobs;
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly AlertSettingsProvider _alerts;
        private readonly Microsoft.Extensions.Options.IOptions<Cafe.API.Options.TelegramOptions> _telegramOptions;
        private readonly ILogger<SystemController> _logger;

        public SystemController(HealthCheckService health, RecentLogSink logs, JobMonitor jobs, ApplicationDbContext context,
            IWebHostEnvironment env, AlertSettingsProvider alerts, Microsoft.Extensions.Options.IOptions<Cafe.API.Options.TelegramOptions> telegramOptions,
            ILogger<SystemController> logger)
        {
            _alerts = alerts;
            _telegramOptions = telegramOptions;
            _health = health;
            _logs = logs;
            _jobs = jobs;
            _context = context;
            _env = env;
            _logger = logger;
        }

        public static string Version =>
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";

        // GET: api/admin/system — состояние сервера, базы, фоновых задач и последние ошибки
        [HttpGet("api/admin/system")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> Get([FromQuery] int take = 100, CancellationToken ct = default)
        {
            var report = await _health.CheckHealthAsync(ct);
            long? dbSize = null;
            try
            {
                dbSize = (await _context.Database.SqlQueryRaw<long>("SELECT pg_database_size(current_database()) AS \"Value\"").ToListAsync(ct)).FirstOrDefault();
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogDebug(ex, "Размер базы не получен"); }

            var process = Process.GetCurrentProcess();
            return Ok(new
            {
                status = report.Status.ToString(),
                version = Version,
                environment = _env.EnvironmentName,
                startedAt = StartedAt,
                uptimeMinutes = (int)(DateTime.UtcNow - StartedAt).TotalMinutes,
                memoryMb = process.WorkingSet64 / 1024 / 1024,
                dbSizeMb = dbSize / 1024 / 1024,
                checks = report.Entries.Select(e => new
                {
                    name = e.Key,
                    title = AlertService.Title(e.Key),
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description ?? e.Value.Exception?.Message,
                    durationMs = (int)e.Value.Duration.TotalMilliseconds,
                }),
                jobs = _jobs.All(),
                logs = _logs.Snapshot(Math.Clamp(take, 1, 300)),
            });
        }

        // GET: api/admin/system/endpoints?minutes=60 — стабильность ручек API за окно (до суток)
        [HttpGet("api/admin/system/endpoints")]
        [HasPermission(Permissions.Settings)]
        public IActionResult Endpoints([FromServices] EndpointStats stats, [FromQuery] int minutes = 60)
        {
            var window = TimeSpan.FromMinutes(Math.Clamp(minutes, 5, 24 * 60));
            return Ok(new
            {
                minutes = (int)window.TotalMinutes,
                // Данные копятся с момента запуска сервера
                since = StartedAt,
                alertRule = new { minRequests = EndpointsHealthCheck.MinRequests, maxErrorRate = EndpointsHealthCheck.MaxErrorRate, windowMinutes = 15 },
                items = stats.Summary(window).OrderByDescending(e => e.Errors5xx).ThenByDescending(e => e.P95Ms),
            });
        }

        public class AlertsRequest
        {
            /// <summary>Адрес пинга внешнего мониторинга (Healthchecks.io и т.п.); пусто — выключить</summary>
            public string? PingUrl { get; set; }
            /// <summary>Оповещать об ошибках на сайте; null — не менять</summary>
            public bool? SiteNotify { get; set; }
        }

        // GET: api/admin/system/alerts — внешний мониторинг: адрес пинга и адрес проверки сайта
        [HttpGet("api/admin/system/alerts")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> GetAlerts() => Ok(new
        {
            pingUrl = await _alerts.PingUrlAsync(),
            siteNotify = (await _alerts.GetAsync()).SiteNotify,
            healthUrl = $"{Request.Scheme}://{Request.Host}/health/full",
        });

        // PUT: api/admin/system/alerts — адрес пинга
        [HttpPut("api/admin/system/alerts")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> SaveAlerts([FromBody] AlertsRequest r)
        {
            var url = string.IsNullOrWhiteSpace(r.PingUrl) ? null : r.PingUrl.Trim();
            if (url != null && (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != "https" || url.Length > 500))
                return BadRequest(new { message = "Адрес пинга — https://… из сервиса мониторинга (например, https://hc-ping.com/…)" });
            var current = await _alerts.GetAsync();
            await _alerts.SaveAsync(new AlertSettings
            {
                PingUrl = url,
                SiteNotify = r.SiteNotify ?? current.SiteNotify,
                TelegramChatId = current.TelegramChatId,
            }, User.GetUserId());
            return await GetAlerts();
        }

        // GET: api/admin/system/errors?since=… — ошибки сервера после since: для значка оповещений на сайте
        [HttpGet("api/admin/system/errors")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> Errors([FromQuery] DateTime? since)
        {
            var enabled = (await _alerts.GetAsync()).SiteNotify;
            if (!enabled) return Ok(new { enabled, count = 0, latest = Array.Empty<object>(), now = DateTime.UtcNow });
            var from = since?.ToUniversalTime() ?? DateTime.UtcNow.AddHours(-24);
            var errors = _logs.Snapshot(300)
                .Where(e => (e.Level == "Error" || e.Level == "Fatal") && e.At > from)
                .OrderByDescending(e => e.At).ToList();
            return Ok(new
            {
                enabled,
                count = errors.Count,
                latest = errors.Take(5).Select(e => new { e.At, e.Message, source = e.Source?.Split('.').Last(), e.RequestId }),
                now = DateTime.UtcNow,
            });
        }

        // POST: api/admin/system/alerts/test — проверочный пинг: в сервисе мониторинга появится «пинг получен»
        [HttpPost("api/admin/system/alerts/test")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> TestAlert([FromServices] AlertService alerts)
        {
            var url = await _alerts.PingUrlAsync();
            if (url == null) return BadRequest(new { message = "Сначала укажите адрес пинга" });
            try
            {
                var problems = await alerts.ProblemsAsync(HttpContext.RequestAborted);
                var ok = await alerts.PingAsync(url, problems.Count == 0 ? null : string.Join('\n', problems), HttpContext.RequestAborted);
                return ok
                    ? Ok(new { message = problems.Count == 0 ? "Пинг отправлен — в сервисе мониторинга проверка станет зелёной" : $"Отправлено «есть проблемы»: {string.Join("; ", problems)}" })
                    : BadRequest(new { message = "Сервис мониторинга не принял пинг — проверьте адрес" });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return BadRequest(new { message = $"Сервис мониторинга недоступен: {ex.Message}" });
            }
        }

        public class ClientLogRequest
        {
            public string Message { get; set; } = string.Empty;
            public string? Stack { get; set; }
            public string? Url { get; set; }
            public string? UserAgent { get; set; }
        }

        // POST: api/client-log — ошибка в браузере (сайт, касса, кухня); пишется в журнал сервера
        [HttpPost("api/client-log")]
        [AllowAnonymous]
        [EnableRateLimiting("client-log")]
        public IActionResult ClientLog([FromBody] ClientLogRequest r)
        {
            static string Cut(string? s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max];
            // Адрес без параметров: в них бывают токены
            var url = Cut(r.Url, 300);
            var q = url.IndexOf('?');
            if (q >= 0) url = url[..q];
            _logger.LogWarning("Ошибка в браузере: {ClientMessage} на {ClientUrl} (пользователь {UserId}, {UserAgent})\n{ClientStack}",
                Cut(r.Message, 500), url, User.GetUserId(), Cut(r.UserAgent ?? Request.Headers.UserAgent.ToString(), 200), Cut(r.Stack, 2000));
            return NoContent();
        }
    }
}
