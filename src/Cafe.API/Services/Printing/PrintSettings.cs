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
        /// <summary>
        /// agent — принтер по USB к кассовому ПК, печатает программа на ПК (принтер Windows);
        /// agent-lan — принтер в сети кафе (LAN), печатает программа на ПК по его IP (сервер в интернете);
        /// network — принтер в сети кафе, сервер печатает прямо на него (сервер в той же сети)
        /// </summary>
        public string Mode { get; set; } = "agent";
        /// <summary>
        /// Касса АТОЛ и УТМ ЕГАИС — через программу на кассовом ПК. Нужно, когда сервер в интернете:
        /// сам он до устройств в сети кафе не достаёт, а программа на ПК — достаёт.
        /// </summary>
        public bool RelayDevices { get; set; }
        /// <summary>
        /// Касса — напрямую через драйвер ККТ 10 на кассовом ПК (как у Interpos), без АТОЛ Web Server:
        /// программа связи сама выполняет задания кассы. Работает вместе с RelayDevices.
        /// </summary>
        public bool AtolViaDriver { get; set; }
        /// <summary>IP принтера в сети (режим network)</summary>
        public string? Host { get; set; }
        public int Port { get; set; } = 9100;
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
        /// <summary>Вид счёта для гостя; null — по умолчанию (с подвалом из Footer)</summary>
        public PrecheckLayout? Layout { get; set; }
    }

    /// <summary>Строка шапки или подвала счёта</summary>
    public class PrecheckLayoutLine
    {
        public string Text { get; set; } = string.Empty;
        /// <summary>left, center, right</summary>
        public string Align { get; set; } = "center";
        public bool Bold { get; set; }
        /// <summary>Крупный шрифт (двойная ширина и высота)</summary>
        public bool Large { get; set; }
    }

    /// <summary>
    /// Вид счёта для гостя (пречек). Блюда, суммы и строка «Это не кассовый чек» печатаются всегда;
    /// шапка, подвал, какие поля показывать и подписи — настраиваются. В тексте: {brand}, {title}, {table}, {check}, {waiter}, {guests}, {date}, {time}.
    /// </summary>
    public class PrecheckLayout
    {
        public const int MaxLines = 12;
        public const int MaxText = 64;
        public const int MaxLabel = 24;

        public List<PrecheckLayoutLine> Header { get; set; } = new();
        public bool ShowDate { get; set; } = true;
        public bool ShowGuests { get; set; } = true;
        public string GuestsLabel { get; set; } = "Гостей";
        public bool ShowWaiter { get; set; } = true;
        public string WaiterLabel { get; set; } = "Официант";
        /// <summary>Количество и цена под названием блюда</summary>
        public bool ShowPrices { get; set; } = true;
        public string SubtotalLabel { get; set; } = "Сумма блюд";
        public string TotalLabel { get; set; } = "ИТОГО";
        public string DueLabel { get; set; } = "К ОПЛАТЕ";
        public List<PrecheckLayoutLine> Footer { get; set; } = new();

        public static PrecheckLayout Default(string? footer) => new()
        {
            Header = new()
            {
                new() { Text = "{brand}", Align = "center", Bold = true, Large = true },
                new() { Text = "{title}", Align = "center", Bold = true },
                new() { Text = "{table} · {check}", Align = "center" },
            },
            Footer = string.IsNullOrWhiteSpace(footer) ? new() : new() { new() { Text = footer.Trim(), Align = "center" } },
        };

        public static readonly string[] Aligns = { "left", "center", "right" };

        public string? Validate()
        {
            if (Header.Count > MaxLines || Footer.Count > MaxLines) return $"Не больше {MaxLines} строк в шапке и внизу";
            foreach (var l in Header.Concat(Footer))
            {
                if ((l.Text ?? "").Length > MaxText) return $"Строка длиннее {MaxText} символов";
                if (!Aligns.Contains(l.Align)) return "Неизвестное выравнивание";
            }
            foreach (var label in new[] { GuestsLabel, WaiterLabel, SubtotalLabel, TotalLabel, DueLabel })
                if ((label ?? "").Length > MaxLabel) return $"Подпись длиннее {MaxLabel} символов";
            if (string.IsNullOrWhiteSpace(TotalLabel)) return "Подпись итога не может быть пустой";
            return null;
        }

        public PrecheckLayout Normalized() => new()
        {
            Header = Header.Select(Clean).ToList(),
            Footer = Footer.Select(Clean).ToList(),
            ShowDate = ShowDate, ShowGuests = ShowGuests, ShowWaiter = ShowWaiter, ShowPrices = ShowPrices,
            GuestsLabel = (GuestsLabel ?? "").Trim(), WaiterLabel = (WaiterLabel ?? "").Trim(),
            SubtotalLabel = string.IsNullOrWhiteSpace(SubtotalLabel) ? "Сумма блюд" : SubtotalLabel.Trim(),
            TotalLabel = (TotalLabel ?? "").Trim(),
            DueLabel = string.IsNullOrWhiteSpace(DueLabel) ? "К ОПЛАТЕ" : DueLabel.Trim(),
        };

        private static PrecheckLayoutLine Clean(PrecheckLayoutLine l) => new() { Text = (l.Text ?? "").TrimEnd(), Align = l.Align, Bold = l.Bold, Large = l.Large };
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

        /// <summary>Программа на кассовом ПК заходила за заданиями меньше минуты назад</summary>
        public static bool AgentOnline => AgentSeenAt > DateTime.UtcNow.AddSeconds(-60);

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
