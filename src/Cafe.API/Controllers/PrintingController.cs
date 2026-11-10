using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Authorization;
using Cafe.API.Data;
using Cafe.API.Extensions;
using Cafe.API.Models;
using Cafe.API.Services.Printing;

namespace Cafe.API.Controllers
{
    /// <summary>
    /// Программа печати на кассовом ПК: забирает задания (счета для гостей) и отправляет их на принтер.
    /// Вход — по ключу из настроек принтера (заголовок X-Print-Token), без пользователя.
    /// </summary>
    [ApiController]
    [Route("api/print-agent")]
    [AllowAnonymous]
    public class PrintAgentController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly PrintSettingsProvider _settings;
        private readonly PrintService _print;
        private readonly Cafe.API.Services.Fiscal.FiscalSettingsProvider _fiscal;

        public PrintAgentController(ApplicationDbContext context, PrintSettingsProvider settings, PrintService print,
            Cafe.API.Services.Fiscal.FiscalSettingsProvider fiscal)
        {
            _context = context;
            _settings = settings;
            _print = print;
            _fiscal = fiscal;
        }

        private async Task<PrintSettings?> AuthAsync()
        {
            var s = await _settings.GetAsync();
            var token = Request.Headers["X-Print-Token"].ToString();
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(s.AgentToken)) return null;
            var ok = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(s.AgentToken));
            return ok ? s : null;
        }

        // GET: api/print-agent/jobs?wait=20 — ждёт до wait секунд, пока не появится задание:
        // счёт на печать (если печатает программа) или запрос к кассе/УТМ (если они «через программу»)
        [HttpGet("jobs")]
        public async Task<IActionResult> Jobs([FromQuery] int wait = 20, CancellationToken ct = default)
        {
            var s = await AuthAsync();
            if (s == null) return Unauthorized(new { message = "Неверный ключ программы — скачайте её заново в настройках принтера" });
            PrintSettingsProvider.AgentSeenAt = DateTime.UtcNow;
            PrintSettingsProvider.AgentHost = Request.Headers["X-Print-Host"].ToString() is { Length: > 0 and <= 100 } h ? h : null;
            await _print.ExpireStaleAsync();

            var deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(wait, 0, 25));
            while (true)
            {
                s = await _settings.GetAsync();
                var printHere = s.Enabled && s.Mode is "agent" or "agent-lan";
                var relay = s.RelayDevices;
                var jobs = printHere || relay
                    ? await _context.PrintJobs
                        .Where(j => j.Status == PrintJobStatuses.Pending && (j.Kind == "http" ? relay : printHere))
                        .OrderBy(j => j.Id).Take(5).ToListAsync(ct)
                    : new List<PrintJob>();
                if (jobs.Count > 0)
                {
                    foreach (var j in jobs)
                    {
                        j.Status = PrintJobStatuses.Taken;
                        j.TakenAt = DateTime.UtcNow;
                    }
                    await _context.SaveChangesAsync(ct);
                    return Ok(AgentReply(s, jobs, await AtolBaseAsync(s)));
                }
                if (DateTime.UtcNow >= deadline || ct.IsCancellationRequested) break;
                // Запросы к кассе ждут ответа — забираем их почти сразу
                try { await Task.Delay(250, ct); } catch (TaskCanceledException) { break; }
                PrintSettingsProvider.AgentSeenAt = DateTime.UtcNow;
            }
            return Ok(AgentReply(s, new List<PrintJob>(), await AtolBaseAsync(s)));
        }

        /// <summary>Адрес кассы из настроек — запросы на него программа выполняет через драйвер, если касса «через драйвер»</summary>
        private async Task<string?> AtolBaseAsync(PrintSettings s) =>
            s.RelayDevices && s.AtolViaDriver ? (await _fiscal.GetAsync()).Url.Trim().TrimEnd('/') : null;

        private static object AgentReply(PrintSettings s, List<PrintJob> jobs, string? atolDriverBase) => new
        {
            printer = s.PrinterName,
            // Касса через драйвер ККТ 10: запросы к этому адресу программа выполняет сама, без АТОЛ Web Server
            atolDriverBase,
            // Принтер в сети кафе: программа печатает на его IP
            printerHost = s.Mode == "agent-lan" ? s.Host : null,
            printerPort = s.Port,
            jobs = jobs.Select(j => new { j.Id, type = j.Kind == "http" ? "http" : "print", j.Title, j.Payload })
        };

        public class ResultRequest
        {
            public bool Ok { get; set; }
            public string? Error { get; set; }
            /// <summary>Ответ устройства на http-запрос</summary>
            public int? Status { get; set; }
            public string? ContentType { get; set; }
            public string? Body { get; set; }
        }

        // POST: api/print-agent/jobs/{id}/result — напечатано / ответ устройства / ошибка
        [HttpPost("jobs/{id:int}/result")]
        public async Task<IActionResult> Result(int id, [FromBody] ResultRequest r)
        {
            if (await AuthAsync() == null) return Unauthorized();
            var job = await _context.PrintJobs.FirstOrDefaultAsync(j => j.Id == id);
            if (job == null) return NotFound();
            job.Status = r.Ok ? PrintJobStatuses.Done : PrintJobStatuses.Error;
            var err = r.Error?.Trim();
            job.Error = r.Ok ? null : (string.IsNullOrEmpty(err) ? "Ошибка устройства" : err[..Math.Min(300, err.Length)]);
            if (r.Ok && job.Kind == "http")
                job.Result = System.Text.Json.JsonSerializer.Serialize(new DeviceRelayHandler.RelayResponse(r.Status ?? 200, r.ContentType, r.Body),
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            job.DoneAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok();
        }
    }

    /// <summary>Настройка принтера счетов и программы печати</summary>
    [ApiController]
    [Route("api/admin/printing")]
    [HasPermission(Permissions.Settings)]
    public class PrintingController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly PrintSettingsProvider _settings;
        private readonly PrintService _print;

        public PrintingController(ApplicationDbContext context, PrintSettingsProvider settings, PrintService print)
        {
            _context = context;
            _settings = settings;
            _print = print;
        }

        public class SaveRequest
        {
            public bool Enabled { get; set; }
            public string Mode { get; set; } = "agent";
            public bool RelayDevices { get; set; }
            public bool AtolViaDriver { get; set; }
            public string? Host { get; set; }
            public int Port { get; set; } = 9100;
            public string PrinterName { get; set; } = string.Empty;
            public int Width { get; set; } = 48;
            public int CodeTable { get; set; } = 17;
            public int Encoding { get; set; } = 866;
            public string? Footer { get; set; }
        }

        private async Task<object> StateAsync()
        {
            var s = await _settings.GetAsync();
            var recent = await _context.PrintJobs.AsNoTracking().Where(j => j.Kind != "http").OrderByDescending(j => j.Id).Take(10)
                .Select(j => new { j.Id, j.Kind, j.Title, j.Status, j.Error, j.CreatedAt, j.DoneAt }).ToListAsync();
            return new
            {
                s.Enabled, s.Mode, s.RelayDevices, s.AtolViaDriver, s.Host, s.Port, s.PrinterName, s.Width, s.CodeTable, s.Encoding, s.Footer,
                layout = s.Layout ?? PrecheckLayout.Default(s.Footer),
                agentSeenAt = PrintSettingsProvider.AgentSeenAt,
                agentHost = PrintSettingsProvider.AgentHost,
                recent
            };
        }

        // GET: api/admin/printing
        [HttpGet]
        public async Task<IActionResult> Get() => Ok(await StateAsync());

        // PUT: api/admin/printing
        [HttpPut]
        public async Task<IActionResult> Save([FromBody] SaveRequest r)
        {
            if (r.Mode is not ("agent" or "agent-lan" or "network")) return BadRequest(new { message = "Неизвестный способ подключения" });
            if (r.Enabled && r.Mode is "network" or "agent-lan" && !System.Net.IPAddress.TryParse(r.Host?.Trim(), out _))
                return BadRequest(new { message = "Укажите IP-адрес принтера, например 192.168.1.87" });
            if (r.Port is < 1 or > 65535) return BadRequest(new { message = "Порт — от 1 до 65535 (обычно 9100)" });
            if (r.Enabled && r.Mode == "agent" && string.IsNullOrWhiteSpace(r.PrinterName)) return BadRequest(new { message = "Укажите имя принтера в Windows" });
            if (r.Width is < 24 or > 64) return BadRequest(new { message = "Ширина строки — от 24 до 64 символов" });
            if (r.CodeTable is < 0 or > 255) return BadRequest(new { message = "Кодовая таблица — число от 0 до 255" });
            if (r.Encoding is not (866 or 1251)) return BadRequest(new { message = "Кодировка — 866 или 1251" });
            var s = await _settings.GetAsync();
            await _settings.SaveAsync(new PrintSettings
            {
                Enabled = r.Enabled,
                Mode = r.Mode,
                RelayDevices = r.RelayDevices,
                AtolViaDriver = r.AtolViaDriver,
                Host = string.IsNullOrWhiteSpace(r.Host) ? null : r.Host.Trim(),
                Port = r.Port,
                PrinterName = string.IsNullOrWhiteSpace(r.PrinterName) ? s.PrinterName : r.PrinterName.Trim(),
                Width = r.Width,
                CodeTable = r.CodeTable,
                Encoding = r.Encoding,
                Footer = string.IsNullOrWhiteSpace(r.Footer) ? null : r.Footer.Trim()[..Math.Min(200, r.Footer.Trim().Length)],
                AgentToken = s.AgentToken,
                Layout = s.Layout,
            }, User.GetUserId());
            return Ok(await StateAsync());
        }

        public class PreviewRequest
        {
            public int Width { get; set; } = 48;
            public PrecheckLayout? Layout { get; set; }
        }

        /// <summary>Пример счёта: стол, официант, три блюда, скидка</summary>
        private static async Task<PrecheckData> SampleAsync(Cafe.API.Services.BrandSettingsProvider brand)
        {
            var now = DateTime.UtcNow;
            return new PrecheckData((await brand.GetAsync()).DisplayName, "5", "счёт №1024", "Анна", 2, now.AddMinutes(-40), now,
                new[]
                {
                    new PrecheckLine("Шаурма классическая", 2, 350, 700),
                    new PrecheckLine("Шашлык из свиной шеи · 250 г", 1, 690, 690),
                    new PrecheckLine("Морс клюквенный 0,5 л", 2, 150, 300),
                },
                null, Adjustments: new[] { new PrecheckAdjustment("Скидка 10%", -169) });
        }

        // POST: api/admin/printing/preview — как выглядит счёт для гостя с этим видом (пример)
        [HttpPost("preview")]
        public async Task<IActionResult> Preview([FromBody] PreviewRequest r, [FromServices] Cafe.API.Services.BrandSettingsProvider brand)
        {
            var cur = await _settings.GetAsync();
            var layout = (r.Layout ?? cur.Layout ?? PrecheckLayout.Default(cur.Footer)).Normalized();
            if (layout.Validate() is string err) return BadRequest(new { message = err });
            var s = new PrintSettings { Width = Math.Clamp(r.Width, 24, 64), CodeTable = 17, Encoding = 866, Layout = layout };
            return Ok(new { width = s.Width, lines = PrintService.Preview(PrintService.Precheck(s, await SampleAsync(brand)), s.Encoding) });
        }

        // PUT: api/admin/printing/layout — вид счёта для гостя
        [HttpPut("layout")]
        public async Task<IActionResult> SaveLayout([FromBody] PrecheckLayout layout)
        {
            var l = layout.Normalized();
            if (l.Validate() is string err) return BadRequest(new { message = err });
            var s = await _settings.GetAsync();
            s.Layout = l;
            s.Footer = string.Join(" ", l.Footer.Select(x => x.Text).Where(x => x.Length > 0)) is { Length: > 0 } f ? f[..Math.Min(200, f.Length)] : null;
            await _settings.SaveAsync(s, User.GetUserId());
            return Ok(await StateAsync());
        }

        // POST: api/admin/printing/layout/sample — напечатать пример счёта с этим видом
        [HttpPost("layout/sample")]
        public async Task<IActionResult> PrintSample([FromBody] PrecheckLayout layout, [FromServices] Cafe.API.Services.BrandSettingsProvider brand)
        {
            var s = await _settings.GetAsync();
            if (!s.Enabled) return BadRequest(new { message = "Сначала включите принтер счетов и сохраните настройки" });
            var l = layout.Normalized();
            if (l.Validate() is string err) return BadRequest(new { message = err });
            var copy = JsonSerializer.Deserialize<PrintSettings>(JsonSerializer.Serialize(s))!;
            copy.Layout = l;
            var job = await _print.EnqueueAsync("test", null, "Пример счёта", PrintService.Precheck(copy, await SampleAsync(brand)), User.GetUserId());
            return Ok(new { job.Id });
        }

        // POST: api/admin/printing/test — проверочная страница
        [HttpPost("test")]
        public async Task<IActionResult> Test()
        {
            var s = await _settings.GetAsync();
            if (!s.Enabled) return BadRequest(new { message = "Сначала включите принтер счетов и сохраните настройки" });
            var job = await _print.EnqueueAsync("test", null, "Проверка печати", PrintService.TestPage(s), User.GetUserId());
            return Ok(new { job.Id });
        }

        // POST: api/admin/printing/token — новый ключ (старая программа перестанет работать)
        [HttpPost("token")]
        public async Task<IActionResult> NewToken()
        {
            var s = await _settings.GetAsync();
            s.AgentToken = PrintSettingsProvider.NewToken();
            await _settings.SaveAsync(s, User.GetUserId());
            return Ok();
        }

        // GET: api/admin/printing/agent?base=http://192.168.1.10:5173 — программа печати для кассового ПК (PowerShell)
        [HttpGet("agent")]
        public async Task<IActionResult> Agent([FromQuery(Name = "base")] string? baseUrl)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                return BadRequest(new { message = "Не указан адрес сервера" });
            var s = await _settings.GetAsync();
            var script = PrintAgentScript.Build(uri.GetLeftPart(UriPartial.Authority), s.AgentToken);
            // PowerShell 5 читает скрипт как UTF-8 только с BOM
            var bytes = new UTF8Encoding(true).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(script)).ToArray();
            return File(bytes, "application/octet-stream", "cafe-print.ps1");
        }
    }
}
