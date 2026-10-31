using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Cafe.API.Data;
using Cafe.API.Models;

namespace Cafe.API.Services.Egais
{
    /// <summary>
    /// Настройки ЕГАИС: задаются в админке (таблица app_settings), а если там пусто — берутся из appsettings (Egais).
    /// Держатся в памяти; после сохранения действуют сразу, без перезапуска.
    /// </summary>
    public class EgaisSettingsProvider
    {
        public const string Key = "egais";
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly IServiceScopeFactory _scopes;
        private readonly EgaisOptions _fromConfig;
        private EgaisOptions? _current;

        public EgaisSettingsProvider(IServiceScopeFactory scopes, IOptions<EgaisOptions> config)
        {
            _scopes = scopes;
            _fromConfig = config.Value;
        }

        /// <summary>Текущие настройки (до первой загрузки из базы — из appsettings)</summary>
        public EgaisOptions Current => _current ?? _fromConfig;

        /// <summary>Сохранены ли настройки в админке (иначе действуют значения из appsettings)</summary>
        public bool FromDatabase { get; private set; }
        public DateTime? UpdatedAt { get; private set; }

        public async Task LoadAsync()
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key);
            if (row == null)
            {
                _current = _fromConfig;
                FromDatabase = false;
                return;
            }
            _current = JsonSerializer.Deserialize<EgaisOptions>(row.Value, Json) ?? _fromConfig;
            FromDatabase = true;
            UpdatedAt = row.UpdatedAt;
        }

        public async Task SaveAsync(EgaisOptions options, int? userId)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == Key);
            if (row == null)
            {
                row = new AppSetting { Key = Key };
                db.AppSettings.Add(row);
            }
            row.Value = JsonSerializer.Serialize(options, Json);
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedByUserId = userId;
            await db.SaveChangesAsync();

            _current = options;
            FromDatabase = true;
            UpdatedAt = row.UpdatedAt;
        }

        /// <summary>Проверка введённых значений; null — всё в порядке</summary>
        public static string? Validate(EgaisOptions o)
        {
            if (!Uri.TryCreate(o.UtmUrl?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return "Адрес УТМ должен начинаться с http:// или https://, например http://localhost:8080";
            if (o.Enabled && !System.Text.RegularExpressions.Regex.IsMatch(o.FsrarId ?? "", @"^\d{3,12}$"))
                return "ФСРАР ID — только цифры, как на главной странице УТМ";
            if (o.AutoSyncMinutes is < 0 or > 1440)
                return "Интервал автопроверки — от 0 до 1440 минут";
            return null;
        }
    }
}
