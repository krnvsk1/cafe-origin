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

    /// <summary>Счёт закрыт на сотрудника: кто и почему — сумма к удержанию</summary>
    public record PrecheckStaff(string Name, string Reason);

    public record PrecheckData(
        string Brand, string Table, string CheckLabel, string? Waiter, int? Guests,
        DateTime OpenedUtc, DateTime PrintedUtc, IReadOnlyList<PrecheckLine> Lines, string? Footer, PrecheckStaff? Staff = null,
        string? Title = null, IReadOnlyList<PrecheckAdjustment>? Adjustments = null, IReadOnlyList<string>? Info = null);

    /// <summary>Строка после блюд: скидка (минус), обслуживание (плюс), предоплата (минус — итог «к оплате»)</summary>
    public record PrecheckAdjustment(string Name, decimal Amount, bool IsPrepaid = false);

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
            p.Line(d.Title ?? (d.Staff == null ? "СЧЁТ" : "СЧЁТ"), EscPos.Align.Center, bold: true);
            p.Line(string.IsNullOrWhiteSpace(d.Table) ? d.CheckLabel : $"{Cafe.API.Controllers.WaiterController.TableLabel(d.Table)} · {d.CheckLabel}", EscPos.Align.Center);
            foreach (var info in d.Info ?? Array.Empty<string>()) p.Line(info, EscPos.Align.Center);
            p.Separator();
            var msk = d.PrintedUtc.AddHours(3);
            p.Pair(msk.ToString("dd.MM.yyyy HH:mm"), d.Guests is > 0 ? $"Гостей: {d.Guests}" : "");
            if (!string.IsNullOrWhiteSpace(d.Waiter)) p.Line($"Официант: {d.Waiter}");
            if (d.Staff != null)
            {
                p.Line($"Закрыл: {d.Staff.Name}", bold: true);
            }
            p.Separator();
            foreach (var l in d.Lines)
            {
                p.Line(l.Name);
                p.Pair($"  {l.Quantity} x {Money(l.Price)}", Money(l.Amount));
            }
            p.Separator();
            var sum = d.Lines.Sum(l => l.Amount);
            var adj = d.Adjustments ?? Array.Empty<PrecheckAdjustment>();
            if (adj.Count > 0)
            {
                p.Pair("Сумма блюд", Money(sum));
                foreach (var a in adj.Where(a => !a.IsPrepaid)) p.Pair(a.Name, (a.Amount < 0 ? "-" : "+") + Money(Math.Abs(a.Amount)));
                sum += adj.Where(a => !a.IsPrepaid).Sum(a => a.Amount);
                if (adj.Any(a => a.IsPrepaid))
                {
                    p.Pair("ИТОГО", Money(sum), bold: true);
                    foreach (var a in adj.Where(a => a.IsPrepaid)) p.Pair(a.Name, "-" + Money(Math.Abs(a.Amount)));
                    sum += adj.Where(a => a.IsPrepaid).Sum(a => a.Amount);
                }
            }
            p.Pair(d.Staff != null ? "" : adj.Any(a => a.IsPrepaid) ? "К ОПЛАТЕ" : "ИТОГО", Money(Math.Max(0, sum)), bold: true);
            p.Separator('=');
            p.Line("", EscPos.Align.Center);
            if (d.Staff != null)
            {
                p.Line("", EscPos.Align.Center);
            }
            else if (!string.IsNullOrWhiteSpace(d.Footer)) p.Line(d.Footer!, EscPos.Align.Center);
            return p.Cut().ToArray();
        }

        /// <summary>Строка документа для предпросмотра на экране</summary>
        public record PreviewLine(string Text, int Align, bool Bold, bool Big);

        /// <summary>
        /// Предпросмотр: разбирает ESC/POS обратно в строки с выравниванием, жирным и крупным шрифтом —
        /// так в настройках видно, как счёт выйдет на ленте
        /// </summary>
        public static List<PreviewLine> Preview(byte[] bytes, int encoding)
        {
            var enc = System.Text.Encoding.GetEncoding(encoding);
            var lines = new List<PreviewLine>();
            var buf = new List<byte>();
            int align = 0; bool bold = false, big = false;
            void Flush() { lines.Add(new PreviewLine(enc.GetString(buf.ToArray()), align, bold, big)); buf.Clear(); }
            for (var i = 0; i < bytes.Length; i++)
            {
                var b = bytes[i];
                if (b == 0x1B && i + 1 < bytes.Length)
                {
                    var cmd = bytes[++i];
                    if (cmd == 0x40) continue;                                              // ESC @
                    if (i + 1 >= bytes.Length) break;
                    var n = bytes[++i];
                    if (cmd == 0x61) align = n;                                             // ESC a
                    else if (cmd == 0x45) bold = n != 0;                                    // ESC E
                    else if (cmd == 0x64) for (var k = 0; k < n; k++) lines.Add(new PreviewLine("", 0, false, false)); // ESC d
                    continue;                                                               // ESC t и прочие — с одним параметром
                }
                if (b == 0x1D && i + 1 < bytes.Length)
                {
                    var cmd = bytes[++i];
                    if (cmd == 0x21 && i + 1 < bytes.Length) big = bytes[++i] != 0;          // GS !
                    else if (cmd == 0x56) i += 2;                                           // GS V — обрезка
                    continue;
                }
                if (b == 0x0A) { Flush(); continue; }
                buf.Add(b);
            }
            if (buf.Count > 0) Flush();
            // Отступ перед обрезкой на экране не нужен
            while (lines.Count > 0 && lines[^1].Text.Length == 0) lines.RemoveAt(lines.Count - 1);
            return lines;
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
