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

        public PrintAgentController(ApplicationDbContext context, PrintSettingsProvider settings, PrintService print)
        {
            _context = context;
            _settings = settings;
            _print = print;
        }

        public class ResultRequest
        {
            public bool Ok { get; set; }
            public string? Error { get; set; }
        }

        private async Task<PrintSettings?> AuthAsync()
        {
            var s = await _settings.GetAsync();
            var token = Request.Headers["X-Print-Token"].ToString();
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(s.AgentToken)) return null;
            var ok = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(s.AgentToken));
            return ok ? s : null;
        }

        // GET: api/print-agent/jobs?wait=20 — ждёт до wait секунд, пока не появится задание
        [HttpGet("jobs")]
        public async Task<IActionResult> Jobs([FromQuery] int wait = 20, CancellationToken ct = default)
        {
            var s = await AuthAsync();
            if (s == null) return Unauthorized(new { message = "Неверный ключ программы печати — скачайте её заново в настройках принтера" });
            PrintSettingsProvider.AgentSeenAt = DateTime.UtcNow;
            PrintSettingsProvider.AgentHost = Request.Headers["X-Print-Host"].ToString() is { Length: > 0 and <= 100 } h ? h : null;
            await _print.ExpireStaleAsync();

            var deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(wait, 0, 25));
            while (true)
            {
                var jobs = s.Enabled
                    ? await _context.PrintJobs.Where(j => j.Status == PrintJobStatuses.Pending).OrderBy(j => j.Id).Take(5).ToListAsync(ct)
                    : new List<PrintJob>();
                if (jobs.Count > 0)
                {
                    foreach (var j in jobs)
                    {
                        j.Status = PrintJobStatuses.Taken;
                        j.TakenAt = DateTime.UtcNow;
                    }
                    await _context.SaveChangesAsync(ct);
                    return Ok(new { printer = s.PrinterName, jobs = jobs.Select(j => new { j.Id, j.Title, j.Payload }) });
                }
                if (DateTime.UtcNow >= deadline || ct.IsCancellationRequested) break;
                try { await Task.Delay(1000, ct); } catch (TaskCanceledException) { break; }
                PrintSettingsProvider.AgentSeenAt = DateTime.UtcNow;
            }
            return Ok(new { printer = s.PrinterName, jobs = Array.Empty<object>() });
        }

        // POST: api/print-agent/jobs/{id}/result — напечатано или ошибка принтера
        [HttpPost("jobs/{id:int}/result")]
        public async Task<IActionResult> Result(int id, [FromBody] ResultRequest r)
        {
            if (await AuthAsync() == null) return Unauthorized();
            var job = await _context.PrintJobs.FirstOrDefaultAsync(j => j.Id == id);
            if (job == null) return NotFound();
            job.Status = r.Ok ? PrintJobStatuses.Done : PrintJobStatuses.Error;
            job.Error = r.Ok ? null : (string.IsNullOrWhiteSpace(r.Error) ? "Ошибка принтера" : r.Error.Trim()[..Math.Min(300, r.Error.Trim().Length)]);
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
            public string PrinterName { get; set; } = string.Empty;
            public int Width { get; set; } = 48;
            public int CodeTable { get; set; } = 17;
            public int Encoding { get; set; } = 866;
            public string? Footer { get; set; }
        }

        private async Task<object> StateAsync()
        {
            var s = await _settings.GetAsync();
            var recent = await _context.PrintJobs.AsNoTracking().OrderByDescending(j => j.Id).Take(10)
                .Select(j => new { j.Id, j.Kind, j.Title, j.Status, j.Error, j.CreatedAt, j.DoneAt }).ToListAsync();
            return new
            {
                s.Enabled, s.PrinterName, s.Width, s.CodeTable, s.Encoding, s.Footer,
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
            if (string.IsNullOrWhiteSpace(r.PrinterName)) return BadRequest(new { message = "Укажите имя принтера в Windows" });
            if (r.Width is < 24 or > 64) return BadRequest(new { message = "Ширина строки — от 24 до 64 символов" });
            if (r.CodeTable is < 0 or > 255) return BadRequest(new { message = "Кодовая таблица — число от 0 до 255" });
            if (r.Encoding is not (866 or 1251)) return BadRequest(new { message = "Кодировка — 866 или 1251" });
            var s = await _settings.GetAsync();
            await _settings.SaveAsync(new PrintSettings
            {
                Enabled = r.Enabled,
                PrinterName = r.PrinterName.Trim(),
                Width = r.Width,
                CodeTable = r.CodeTable,
                Encoding = r.Encoding,
                Footer = string.IsNullOrWhiteSpace(r.Footer) ? null : r.Footer.Trim()[..Math.Min(200, r.Footer.Trim().Length)],
                AgentToken = s.AgentToken,
            }, User.GetUserId());
            return Ok(await StateAsync());
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

        // GET: api/print-jobs/{id} лежит в WaiterController; здесь — новый ключ (старая программа перестанет печатать)
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
