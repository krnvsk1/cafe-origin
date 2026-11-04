using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Cafe.API.Data;
using Cafe.API.Models;

namespace Cafe.API.Monitoring
{
    /// <summary>Куда слать тревоги: адрес пинга внешнего мониторинга</summary>
    public class AlertSettings
    {
        /// <summary>Устарело: тревоги больше не уходят в Telegram (поле оставлено, чтобы читались старые настройки)</summary>
        public long? TelegramChatId { get; set; }

        /// <summary>
        /// Адрес пинга внешнего мониторинга (Healthchecks.io и похожие): всё в порядке — GET на адрес раз в минуту,
        /// проблема — POST на адрес/fail с описанием. Перестали приходить пинги (сервер упал) — сервис сам поднимет тревогу.
        /// </summary>
        public string? PingUrl { get; set; }
    }

    /// <summary>
    /// Адрес пинга задаётся в «Состоянии системы» и хранится в app_settings (ключ monitoring_alerts);
    /// пока не задан — берётся Monitoring:PingUrl из настроек сервера.
    /// </summary>
    public class AlertSettingsProvider
    {
        public const string Key = "monitoring_alerts";
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private readonly IServiceScopeFactory _scopes;
        private readonly MonitoringOptions _fallback;
        private AlertSettings? _current;

        public AlertSettingsProvider(IServiceScopeFactory scopes, IOptions<MonitoringOptions> options)
        {
            _scopes = scopes;
            _fallback = options.Value;
        }

        public async Task<string?> PingUrlAsync() => (await GetAsync()).PingUrl is { Length: > 0 } u ? u : _fallback.PingUrl;

        public async Task<AlertSettings> GetAsync()
        {
            if (_current != null) return _current;
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key);
            _current = row == null ? new AlertSettings() : JsonSerializer.Deserialize<AlertSettings>(row.Value, Json) ?? new AlertSettings();
            return _current;
        }

        public async Task SaveAsync(AlertSettings settings, int? userId)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == Key);
            if (row == null)
            {
                row = new AppSetting { Key = Key };
                db.AppSettings.Add(row);
            }
            row.Value = JsonSerializer.Serialize(settings, Json);
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedByUserId = userId;
            await db.SaveChangesAsync();
            _current = settings;
        }
    }
}
