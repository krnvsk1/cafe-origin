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
                dbSize = await _context.Database.SqlQueryRaw<long>("SELECT pg_database_size(current_database()) AS \"Value\"").FirstAsync(ct);
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

        public class AlertsRequest
        {
            /// <summary>ID Telegram-чата для тревог; null — выключить</summary>
            public long? TelegramChatId { get; set; }
        }

        // GET: api/admin/system/alerts — куда приходят тревоги
        [HttpGet("api/admin/system/alerts")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> GetAlerts() => Ok(new
        {
            telegramChatId = await _alerts.ChatIdAsync(),
            telegramEnabled = _telegramOptions.Value.Enabled,
            ordersChatId = _telegramOptions.Value.StaffChatId,
        });

        // PUT: api/admin/system/alerts — чат тревог; чат заказов использовать нельзя
        [HttpPut("api/admin/system/alerts")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> SaveAlerts([FromBody] AlertsRequest r)
        {
            if (r.TelegramChatId != null && r.TelegramChatId == _telegramOptions.Value.StaffChatId)
                return BadRequest(new { message = "Это чат, куда приходят заказы. Для тревог заведите отдельный чат" });
            await _alerts.SaveAsync(new AlertSettings { TelegramChatId = r.TelegramChatId }, User.GetUserId());
            return await GetAlerts();
        }

        // POST: api/admin/system/alerts/test — проверочная тревога в выбранный чат
        [HttpPost("api/admin/system/alerts/test")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> TestAlert([FromServices] Cafe.API.Services.TelegramApi telegram)
        {
            var chatId = await _alerts.ChatIdAsync();
            if (chatId == null) return BadRequest(new { message = "Сначала укажите чат для тревог" });
            if (!telegram.Enabled) return BadRequest(new { message = "Telegram-бот не настроен (Telegram:BotToken)" });
            var ok = await telegram.SendMessageAsync(chatId.Value, "🟢 <b>Проверка тревог</b>
Сюда будут приходить сообщения о сбоях сервера.");
            return ok ? NoContent() : BadRequest(new { message = "Telegram не принял сообщение — проверьте ID чата и что бот добавлен в чат" });
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
