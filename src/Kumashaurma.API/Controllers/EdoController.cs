using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Authorization;
using Kumashaurma.API.Data;
using Kumashaurma.API.Extensions;
using Kumashaurma.API.Models;
using Kumashaurma.API.Services.Edo;

namespace Kumashaurma.API.Controllers
{
    /// <summary>
    /// Электронные накладные (УПД) из СБИС: настройки, загрузка, сопоставление с номенклатурой, приход на склад
    /// </summary>
    [ApiController]
    [Route("api/edo")]
    [HasPermission(Permissions.InventoryView)]
    public class EdoController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly EdoService _edo;
        private readonly SbisClient _sbis;
        private readonly SbisSettingsProvider _settings;

        public EdoController(ApplicationDbContext context, EdoService edo, SbisClient sbis, SbisSettingsProvider settings)
        {
            _context = context;
            _edo = edo;
            _sbis = sbis;
            _settings = settings;
        }

        public class SettingsRequest
        {
            public bool Enabled { get; set; }
            public string Login { get; set; } = string.Empty;
            /// <summary>null или пусто — оставить сохранённый пароль</summary>
            public string? Password { get; set; }
            public string? Account { get; set; }
            public int AutoSyncMinutes { get; set; } = 15;
            public int InitialDays { get; set; } = 14;
        }

        public class ImportRequest
        {
            public List<ImportLineDto> Lines { get; set; } = new();
            /// <summary>Сразу провести приход (иначе — черновик)</summary>
            public bool Post { get; set; } = true;
        }

        public class ImportLineDto
        {
            public int No { get; set; }
            public int? StockItemId { get; set; }
            public decimal Factor { get; set; } = 1;
            public bool Skip { get; set; }
        }

        // GET: api/edo/status
        [HttpGet("status")]
        public async Task<IActionResult> Status()
        {
            var o = await _settings.GetAsync();
            var newCount = await _context.EdoDocuments.CountAsync(d => d.Status == EdoStatuses.New);
            return Ok(new
            {
                o.Enabled,
                o.Login,
                hasPassword = !string.IsNullOrEmpty(o.Password),
                o.Account,
                o.AutoSyncMinutes,
                o.InitialDays,
                o.LastSyncAt,
                o.LastError,
                newCount
            });
        }

        // PUT: api/edo/settings
        [HttpPut("settings")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> SaveSettings([FromBody] SettingsRequest r)
        {
            var old = await _settings.GetAsync();
            var o = Merge(old, r);
            if (o.Enabled && (string.IsNullOrWhiteSpace(o.Login) || string.IsNullOrEmpty(o.Password)))
                return BadRequest(new { message = "Укажите логин и пароль пользователя СБИС" });
            if (o.AutoSyncMinutes is < 0 or > 1440) return BadRequest(new { message = "Интервал проверки — от 0 до 1440 минут" });
            if (o.InitialDays is < 1 or > 90) return BadRequest(new { message = "Глубина первой загрузки — от 1 до 90 дней" });

            if (old.Login != o.Login || old.Password != o.Password || old.Account != o.Account) _sbis.ForgetSession(old);
            await _settings.SaveAsync(o, User.GetUserId());
            return await Status();
        }

        // POST: api/edo/settings/test — проверить вход с введёнными данными (без сохранения)
        [HttpPost("settings/test")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> Test([FromBody] SettingsRequest r)
        {
            var o = Merge(await _settings.GetAsync(), r);
            try
            {
                _sbis.ForgetSession(o);
                await _sbis.AuthenticateAsync(o, HttpContext.RequestAborted);
                return Ok(new { ok = true, message = "Вход в СБИС выполнен" });
            }
            catch (SbisException ex) { return Ok(new { ok = false, message = Hint(ex.Message) }); }
            catch (HttpRequestException ex) { return Ok(new { ok = false, message = $"Нет связи с СБИС: {ex.Message}" }); }
        }

        // POST: api/edo/sync — загрузить новые накладные сейчас
        [HttpPost("sync")]
        public async Task<IActionResult> Sync()
        {
            try
            {
                var r = await _edo.SyncAsync(HttpContext.RequestAborted);
                return Ok(new { received = r.Received, errors = r.Errors });
            }
            catch (EdoException ex) { return BadRequest(new { message = Hint(ex.Message) }); }
        }

        // GET: api/edo/documents?status=new
        [HttpGet("documents")]
        public async Task<IActionResult> Documents([FromQuery] string? status, [FromQuery] int take = 100)
        {
            var q = _context.EdoDocuments.AsNoTracking();
            if (!string.IsNullOrEmpty(status)) q = q.Where(d => d.Status == status);
            var docs = await q.OrderByDescending(d => d.Date).ThenByDescending(d => d.Id)
                .Take(Math.Clamp(take, 1, 500))
                .Select(d => new
                {
                    d.Id, d.Number, d.Date, d.SupplierInn, d.SupplierName, d.Total, d.LinesCount, d.CodesCount,
                    d.Status, d.Error, d.StockDocumentId, d.ReceivedAt, d.ImportedAt
                })
                .ToListAsync();
            return Ok(docs);
        }

        // GET: api/edo/documents/{id} — накладная со строками и подставленными позициями склада
        [HttpGet("documents/{id:int}")]
        public async Task<IActionResult> Document(int id)
        {
            var d = await _context.EdoDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (d == null) return NotFound(new { message = "Накладная не найдена" });

            object? lines = null;
            string? parseError = null;
            try
            {
                var (_, view) = await _edo.ViewAsync(d);
                lines = view.Select(v => new
                {
                    no = v.Line.No,
                    name = v.Line.Name,
                    code = v.Line.Code,
                    gtin = v.Line.Gtin,
                    okei = v.Line.Okei,
                    unitName = v.Line.UnitName,
                    quantity = v.Line.Quantity,
                    price = v.Line.Price,
                    vatRate = v.Line.VatRate,
                    sum = v.Line.Sum,
                    codes = v.Line.Codes,
                    stockItemId = v.StockItemId,
                    stockItemName = v.StockItemName,
                    stockUnit = v.StockUnit,
                    factor = v.Factor,
                    remembered = v.Remembered
                }).ToList();
            }
            catch (Exception ex) when (ex is EdoException or UpdParseException) { parseError = ex.Message; }

            return Ok(new
            {
                d.Id, d.Number, d.Date, d.SupplierInn, d.SupplierKpp, d.SupplierName, d.Total, d.LinesCount, d.CodesCount,
                d.Status, error = d.Error ?? parseError, d.StockDocumentId, d.ReceivedAt, d.ImportedAt, d.FileName,
                lines
            });
        }

        // POST: api/edo/documents/{id}/import — создать приход
        [HttpPost("documents/{id:int}/import")]
        [HasPermission(Permissions.InventoryManage)]
        public async Task<IActionResult> Import(int id, [FromBody] ImportRequest r)
        {
            try
            {
                var receipt = await _edo.ImportAsync(id,
                    r.Lines.Select(l => new EdoService.ImportLine(l.No, l.StockItemId, l.Factor, l.Skip)).ToList(),
                    r.Post, User.GetUserId());
                return Ok(new { stockDocumentId = receipt.Id, posted = r.Post });
            }
            catch (EdoException ex) { return BadRequest(new { message = ex.Message }); }
            catch (UpdParseException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        // POST: api/edo/documents/{id}/ignore — скрыть (не товар для склада)
        [HttpPost("documents/{id:int}/ignore")]
        [HasPermission(Permissions.InventoryManage)]
        public Task<IActionResult> Ignore(int id) => SetStatus(id, EdoStatuses.Ignored);

        // POST: api/edo/documents/{id}/restore — вернуть в новые
        [HttpPost("documents/{id:int}/restore")]
        [HasPermission(Permissions.InventoryManage)]
        public Task<IActionResult> Restore(int id) => SetStatus(id, EdoStatuses.New);

        private async Task<IActionResult> SetStatus(int id, string status)
        {
            var d = await _context.EdoDocuments.FirstOrDefaultAsync(x => x.Id == id);
            if (d == null) return NotFound(new { message = "Накладная не найдена" });
            if (d.StockDocumentId != null) return BadRequest(new { message = "По накладной уже создан приход" });
            if (status == EdoStatuses.New && d.Status == EdoStatuses.Error) return BadRequest(new { message = "Накладную с ошибкой вернуть нельзя" });
            d.Status = status;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        private static SbisOptions Merge(SbisOptions old, SettingsRequest r) => new()
        {
            Enabled = r.Enabled,
            Login = (r.Login ?? "").Trim(),
            Password = string.IsNullOrEmpty(r.Password) ? old.Password : r.Password,
            Account = string.IsNullOrWhiteSpace(r.Account) ? null : r.Account.Trim(),
            AutoSyncMinutes = r.AutoSyncMinutes,
            InitialDays = r.InitialDays,
            LastEventId = old.LastEventId,
            LastEventDate = old.LastEventDate,
            LastSyncAt = old.LastSyncAt,
            LastError = old.LastError
        };

        /// <summary>Подсказка к частым ошибкам входа</summary>
        private static string Hint(string message) =>
            message.Contains("SMS", StringComparison.OrdinalIgnoreCase) || message.Contains("подтвержд", StringComparison.OrdinalIgnoreCase)
                ? $"{message}. Для пользователя, под которым работает сервис, отключите подтверждение входа по SMS в настройках безопасности Saby"
                : message;
    }
}
