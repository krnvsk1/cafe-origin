using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Models;

namespace Cafe.API.Services.Printing
{
    public class PrintException : Exception
    {
        public PrintException(string message) : base(message) { }
    }

    /// <summary>Строка счёта: название (с добавками), количество, цена, сумма</summary>
    public record PrecheckLine(string Name, int Quantity, decimal Price, decimal Amount);

    public record PrecheckData(
        string Brand, string Table, string CheckLabel, string? Waiter, int? Guests,
        DateTime OpenedUtc, DateTime PrintedUtc, IReadOnlyList<PrecheckLine> Lines, string? Footer);

    /// <summary>Очередь печати: сервер готовит документ ESC/POS, программа на кассовом ПК его печатает</summary>
    public class PrintService
    {
        private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
        private readonly ApplicationDbContext _context;
        private readonly PrintSettingsProvider _settings;

        public PrintService(ApplicationDbContext context, PrintSettingsProvider settings)
        {
            _context = context;
            _settings = settings;
        }

        public static string Money(decimal v) => v.ToString("#,0.00", Ru).Replace('\u00A0', ' ').Replace('\u202F', ' ');

        /// <summary>Счёт для гостя (не фискальный)</summary>
        public static byte[] Precheck(PrintSettings s, PrecheckData d)
        {
            var p = new EscPos(s.Width, s.CodeTable, s.Encoding);
            if (!string.IsNullOrWhiteSpace(d.Brand)) p.Line(d.Brand, EscPos.Align.Center, bold: true, big: true);
            p.Line("СЧЁТ", EscPos.Align.Center, bold: true);
            p.Line($"{Cafe.API.Controllers.WaiterController.TableLabel(d.Table)} · {d.CheckLabel}", EscPos.Align.Center);
            p.Separator();
            var msk = d.PrintedUtc.AddHours(3);
            p.Pair(msk.ToString("dd.MM.yyyy HH:mm"), d.Guests is > 0 ? $"Гостей: {d.Guests}" : "");
            if (!string.IsNullOrWhiteSpace(d.Waiter)) p.Line($"Официант: {d.Waiter}");
            p.Separator();
            foreach (var l in d.Lines)
            {
                p.Line(l.Name);
                p.Pair($"  {l.Quantity} x {Money(l.Price)}", Money(l.Amount));
            }
            p.Separator();
            p.Pair("ИТОГО", Money(d.Lines.Sum(l => l.Amount)), bold: true);
            p.Separator('=');
            p.Line("Это не кассовый чек", EscPos.Align.Center);
            if (!string.IsNullOrWhiteSpace(d.Footer)) p.Line(d.Footer!, EscPos.Align.Center);
            return p.Cut().ToArray();
        }

        /// <summary>Проверочная страница: кириллица, ширина строки, обрезка</summary>
        public static byte[] TestPage(PrintSettings s)
        {
            var p = new EscPos(s.Width, s.CodeTable, s.Encoding);
            p.Line("ПРОВЕРКА ПЕЧАТИ", EscPos.Align.Center, bold: true, big: true);
            p.Line($"Принтер: {s.PrinterName}", EscPos.Align.Center);
            p.Separator();
            p.Line("Съешь же ещё этих мягких французских булок, да выпей чаю.");
            p.Line(new string('1', s.Width));
            p.Pair("Ширина строки", $"{s.Width} симв.");
            p.Pair("Кодовая таблица", $"{s.CodeTable} / {s.Encoding}");
            p.Separator();
            p.Line("Если русские буквы — кракозябры, поменяйте", EscPos.Align.Center);
            p.Line("кодовую таблицу в настройках принтера.", EscPos.Align.Center);
            return p.Cut().ToArray();
        }

        public async Task<PrintJob> EnqueueAsync(string kind, int? checkId, string title, byte[] payload, int? userId)
        {
            var job = new PrintJob
            {
                Kind = kind,
                CheckId = checkId,
                Title = title.Length > 100 ? title[..100] : title,
                Payload = Convert.ToBase64String(payload),
                CreatedByUserId = userId,
            };
            _context.PrintJobs.Add(job);
            await _context.SaveChangesAsync();
            return job;
        }

        /// <summary>Задания, которые программа забрала, но так и не отчиталась, — помечаем ошибкой</summary>
        public async Task ExpireStaleAsync()
        {
            var limit = DateTime.UtcNow.AddMinutes(-2);
            var stale = await _context.PrintJobs
                .Where(j => (j.Status == PrintJobStatuses.Taken && j.TakenAt < limit) || (j.Status == PrintJobStatuses.Pending && j.CreatedAt < DateTime.UtcNow.AddMinutes(-30)))
                .ToListAsync();
            foreach (var j in stale)
            {
                j.Status = PrintJobStatuses.Error;
                j.Error = j.TakenAt == null ? "Программа печати на кассовом ПК не забрала задание" : "Программа печати не ответила";
                j.DoneAt = DateTime.UtcNow;
            }
            if (stale.Count > 0) await _context.SaveChangesAsync();
        }

        public async Task<PrintSettings> SettingsAsync() => await _settings.GetAsync();
    }
}
