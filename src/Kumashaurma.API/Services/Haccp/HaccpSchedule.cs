using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Services.Haccp
{
    /// <summary>
    /// График проверок: к какому времени (по Москве) должна быть каждая запись журнала.
    /// Для журналов по оборудованию — каждое время = одна проверка каждого холодильника/точки.
    /// Напоминание появляется, только когда время наступило, а записи нет.
    /// </summary>
    public static class HaccpSchedule
    {
        public const string SettingsKey = "haccp_schedule";
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public static readonly IReadOnlyDictionary<string, string[]> Defaults = new Dictionary<string, string[]>
        {
            [HaccpJournals.Health] = new[] { "09:00" },
            [HaccpJournals.Fridge] = new[] { "09:00", "18:00" },
            [HaccpJournals.Storage] = new[] { "10:00" },
            [HaccpJournals.Finished] = new[] { "12:00" },
            [HaccpJournals.Fryer] = new[] { "11:00" },
            [HaccpJournals.Cleaning] = new[] { "21:00" },
            [HaccpJournals.Ccp] = new[] { "13:00" },
        };

        /// <summary>«9:5», «09:00 » → «09:05»; null — не время</summary>
        public static string? NormalizeTime(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return TimeSpan.TryParseExact(s.Trim(), new[] { @"h\:m", @"hh\:mm", @"h\:mm" }, CultureInfo.InvariantCulture, out var t) && t < TimeSpan.FromDays(1)
                ? $"{t.Hours:00}:{t.Minutes:00}"
                : null;
        }

        /// <summary>Проверка и нормализация графика; ошибка — текстом</summary>
        public static (Dictionary<string, string[]> Schedule, string? Error) Validate(Dictionary<string, string[]>? input)
        {
            var result = new Dictionary<string, string[]>();
            foreach (var (key, times) in input ?? new())
            {
                var journal = HaccpJournals.Find(key);
                if (journal == null || journal.Frequency == "per_receipt") continue;
                var list = new List<string>();
                foreach (var t in times ?? Array.Empty<string>())
                {
                    var n = NormalizeTime(t);
                    if (n == null) return (result, $"«{journal.Short}»: «{t}» — укажите время как ЧЧ:ММ");
                    if (!list.Contains(n)) list.Add(n);
                }
                if (list.Count > 24) return (result, $"«{journal.Short}»: не больше 24 проверок в день");
                list.Sort(StringComparer.Ordinal);
                result[key] = list.ToArray();
            }
            return (result, null);
        }

        public static async Task<Dictionary<string, string[]>> LoadAsync(ApplicationDbContext db)
        {
            var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SettingsKey);
            var saved = row == null ? null : JsonSerializer.Deserialize<Dictionary<string, string[]>>(row.Value, Json);
            var result = Defaults.ToDictionary(kv => kv.Key, kv => kv.Value);
            if (saved != null)
                foreach (var (k, v) in Validate(saved).Schedule) result[k] = v;
            return result;
        }

        public static async Task SaveAsync(ApplicationDbContext db, Dictionary<string, string[]> schedule, int? userId)
        {
            var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == SettingsKey);
            if (row == null)
            {
                row = new AppSetting { Key = SettingsKey };
                db.AppSettings.Add(row);
            }
            row.Value = JsonSerializer.Serialize(schedule, Json);
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedByUserId = userId;
            await db.SaveChangesAsync();
        }

        /// <summary>Сколько проверок из графика уже должно быть сделано к этому моменту (время по Москве)</summary>
        public static int DueCount(IEnumerable<string> times, DateTime nowUtc)
        {
            var now = HaccpJournals.Moscow(nowUtc).TimeOfDay;
            return times.Count(t => TimeSpan.TryParse(t, CultureInfo.InvariantCulture, out var ts) && ts <= now);
        }

        /// <summary>Ближайшее время из графика, которое ещё не наступило</summary>
        public static string? Next(IEnumerable<string> times, DateTime nowUtc)
        {
            var now = HaccpJournals.Moscow(nowUtc).TimeOfDay;
            return times.FirstOrDefault(t => TimeSpan.TryParse(t, CultureInfo.InvariantCulture, out var ts) && ts > now);
        }
    }
}
