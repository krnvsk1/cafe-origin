using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Models;

namespace Cafe.API.Services
{
    /// <summary>Название заведения и подзаголовок — показываются на сайте, в оплате и в Telegram-боте</summary>
    public class BrandOptions
    {
        public const string DefaultName = "Доставка еды";

        public string Name { get; set; } = DefaultName;
        /// <summary>Строка под названием на главной</summary>
        public string? Tagline { get; set; }

        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? DefaultName : Name.Trim();
    }

    /// <summary>Настройки названия в app_settings (ключ brand), с кэшем</summary>
    public class BrandSettingsProvider
    {
        public const string Key = "brand";
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private readonly IServiceScopeFactory _scopes;
        private BrandOptions? _current;

        public BrandSettingsProvider(IServiceScopeFactory scopes) => _scopes = scopes;

        public async Task<BrandOptions> GetAsync()
        {
            if (_current != null) return _current;
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key);
            _current = row == null ? new BrandOptions() : JsonSerializer.Deserialize<BrandOptions>(row.Value, Json) ?? new BrandOptions();
            return _current;
        }

        public async Task SaveAsync(BrandOptions options, int? userId)
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
            if (userId != null) row.UpdatedByUserId = userId;
            await db.SaveChangesAsync();
            _current = options;
        }
    }
}
