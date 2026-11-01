using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Models;

namespace Cafe.API.Services.Printing
{
    /// <summary>Принтер счетов для гостя (пречек): чековый принтер по USB к кассовому ПК</summary>
    public class PrintSettings
    {
        public bool Enabled { get; set; }
        /// <summary>Имя принтера в Windows на кассовом ПК (в Interpos — «Printer bar»)</summary>
        public string PrinterName { get; set; } = "Printer bar";
        /// <summary>Символов в строке: 80 мм — 48, 58 мм — 32</summary>
        public int Width { get; set; } = 48;
        /// <summary>Номер кодовой таблицы на принтере (ESC t n): у RONGTA PC866 — обычно 17</summary>
        public int CodeTable { get; set; } = 17;
        /// <summary>Кодировка текста: 866 (DOS) или 1251 (Windows)</summary>
        public int Encoding { get; set; } = 866;
        /// <summary>Текст внизу счёта</summary>
        public string? Footer { get; set; } = "Спасибо, что пришли к нам!";
        /// <summary>Ключ программы печати на кассовом ПК</summary>
        public string AgentToken { get; set; } = string.Empty;
    }

    public class PrintSettingsProvider
    {
        public const string Key = "printing";
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private readonly IServiceScopeFactory _scopes;
        private PrintSettings? _current;

        /// <summary>Когда программа печати последний раз заходила за заданиями</summary>
        public static DateTime? AgentSeenAt { get; set; }
        public static string? AgentHost { get; set; }

        public PrintSettingsProvider(IServiceScopeFactory scopes) => _scopes = scopes;

        public async Task<PrintSettings> GetAsync()
        {
            if (_current != null) return _current;
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key);
            var s = row == null ? new PrintSettings() : JsonSerializer.Deserialize<PrintSettings>(row.Value, Json) ?? new PrintSettings();
            if (string.IsNullOrEmpty(s.AgentToken))
            {
                s.AgentToken = NewToken();
                await SaveAsync(s, null);
            }
            _current = s;
            return s;
        }

        public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

        public async Task SaveAsync(PrintSettings settings, int? userId)
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
