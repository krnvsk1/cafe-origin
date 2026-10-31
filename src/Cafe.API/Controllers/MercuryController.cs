using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Authorization;
using Cafe.API.Data;
using Cafe.API.Extensions;
using Cafe.API.Models;
using Cafe.API.Services.Mercury;

namespace Cafe.API.Controllers
{
    /// <summary>ФГИС «Меркурий»: входящие ВСД и их гашение</summary>
    [ApiController]
    [Route("api/mercury")]
    // Статус и настройки нужны и на странице «Настройка интеграций» (право Settings)
    [HasPermission(Permissions.InventoryView, Permissions.Settings)]
    public class MercuryController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly MercuryService _mercury;
        private readonly MercurySettingsProvider _settings;

        public MercuryController(ApplicationDbContext context, MercuryService mercury, MercurySettingsProvider settings)
        {
            _context = context;
            _mercury = mercury;
            _settings = settings;
        }

        public class SettingsRequest
        {
            public bool Enabled { get; set; }
            public bool Sandbox { get; set; }
            /// <summary>Пусто — оставить сохранённый</summary>
            public string? ApiKey { get; set; }
            public string Login { get; set; } = string.Empty;
            /// <summary>Пусто — оставить сохранённый</summary>
            public string? Password { get; set; }
            public string IssuerId { get; set; } = string.Empty;
            public string InitiatorLogin { get; set; } = string.Empty;
            public string EnterpriseGuid { get; set; } = string.Empty;
            public int AutoSyncMinutes { get; set; } = 30;
            public int InitialDays { get; set; } = 14;
        }

        public class IdsRequest
        {
            public List<int> Ids { get; set; } = new();
        }

        // GET: api/mercury/status
        [HttpGet("status")]
        public async Task<IActionResult> Status()
        {
            var o = await _settings.GetAsync();
            return Ok(new
            {
                o.Enabled,
                o.Sandbox,
                hasApiKey = !string.IsNullOrEmpty(o.ApiKey),
                o.Login,
                hasPassword = !string.IsNullOrEmpty(o.Password),
                o.IssuerId,
                o.InitiatorLogin,
                o.EnterpriseGuid,
                o.AutoSyncMinutes,
                o.InitialDays,
                o.LastSyncAt,
                o.LastError,
                newCount = await _context.MercuryDocuments.CountAsync(d => d.Status == MercuryStatuses.New || d.Status == MercuryStatuses.Error)
            });
        }

        // PUT: api/mercury/settings
        [HttpPut("settings")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> SaveSettings([FromBody] SettingsRequest r)
        {
            var o = Merge(await _settings.GetAsync(), r);
            if (o.Enabled)
            {
                try { VetisClient.Validate(o); }
                catch (VetisException ex) { return BadRequest(new { message = ex.Message }); }
                if (!Guid.TryParse(o.EnterpriseGuid, out _)) return BadRequest(new { message = "GUID площадки — вида 7a0c9a4b-…; он есть в карточке площадки в Цербере" });
            }
            if (o.AutoSyncMinutes is < 0 or > 1440) return BadRequest(new { message = "Интервал проверки — от 0 до 1440 минут" });
            if (o.InitialDays is < 1 or > 31) return BadRequest(new { message = "Глубина загрузки — от 1 до 31 дня" });
            await _settings.SaveAsync(o, User.GetUserId());
            return await Status();
        }

        // POST: api/mercury/settings/test — проверить доступ с введёнными данными (без сохранения)
        [HttpPost("settings/test")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> Test([FromBody] SettingsRequest r)
        {
            var o = Merge(await _settings.GetAsync(), r);
            try
            {
                VetisClient.Validate(o);
                await _mercury.TestAsync(o, HttpContext.RequestAborted);
                return Ok(new { ok = true, message = "Меркурий ответил — доступ к Ветис.API работает" });
            }
            catch (VetisException ex) { return Ok(new { ok = false, message = ex.Message }); }
        }

        // POST: api/mercury/sync — загрузить входящие ВСД сейчас
        [HttpPost("sync")]
        public async Task<IActionResult> Sync()
        {
            try
            {
                var r = await _mercury.SyncAsync(HttpContext.RequestAborted);
                return Ok(new { received = r.Received, total = r.Total, external = r.External });
            }
            catch (VetisException ex) { return BadRequest(new { message = ex.Message }); }
        }

        // GET: api/mercury/documents?status=new
        [HttpGet("documents")]
        public async Task<IActionResult> Documents([FromQuery] string? status, [FromQuery] int take = 200)
        {
            var q = _context.MercuryDocuments.AsNoTracking();
            q = status switch
            {
                null or "" or "all" => q,
                "new" => q.Where(d => d.Status == MercuryStatuses.New || d.Status == MercuryStatuses.Error || d.Status == MercuryStatuses.Processing),
                "done" => q.Where(d => d.Status == MercuryStatuses.Accepted || d.Status == MercuryStatuses.External),
                _ => q.Where(d => d.Status == status)
            };
            var list = await q.OrderByDescending(d => d.IssueDate).ThenByDescending(d => d.Id).Take(Math.Clamp(take, 1, 1000))
                .Select(d => new
                {
                    d.Id, d.Uuid, d.IssueDate, d.VetStatus, d.ProductName, d.Volume, d.UnitName, d.ProductionDate, d.ExpiryDate,
                    d.ConsignorName, d.Waybill, d.Status, d.Error, d.StockEntryGuid, d.ProcessedAt
                })
                .ToListAsync();
            return Ok(list);
        }

        // POST: api/mercury/accept — погасить выбранные ВСД (полностью)
        [HttpPost("accept")]
        [HasPermission(Permissions.InventoryManage)]
        public async Task<IActionResult> Accept([FromBody] IdsRequest r)
        {
            if (r.Ids.Count == 0) return BadRequest(new { message = "Выберите ВСД" });
            if (r.Ids.Count > 50) return BadRequest(new { message = "Не больше 50 ВСД за раз" });
            try
            {
                var res = await _mercury.AcceptAsync(r.Ids, User.GetUserId(), HttpContext.RequestAborted);
                return Ok(new { accepted = res.Accepted, errors = res.Errors });
            }
            catch (VetisException ex) { return BadRequest(new { message = ex.Message }); }
        }

        // POST: api/mercury/documents/{id}/ignore | restore
        [HttpPost("documents/{id:int}/ignore")]
        [HasPermission(Permissions.InventoryManage)]
        public Task<IActionResult> Ignore(int id) => SetStatus(id, MercuryStatuses.Ignored);

        [HttpPost("documents/{id:int}/restore")]
        [HasPermission(Permissions.InventoryManage)]
        public Task<IActionResult> Restore(int id) => SetStatus(id, MercuryStatuses.New);

        private async Task<IActionResult> SetStatus(int id, string status)
        {
            var d = await _context.MercuryDocuments.FirstOrDefaultAsync(x => x.Id == id);
            if (d == null) return NotFound(new { message = "ВСД не найден" });
            if (d.Status is MercuryStatuses.Accepted or MercuryStatuses.Processing) return BadRequest(new { message = "ВСД уже погашен" });
            d.Status = status;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        private static MercuryOptions Merge(MercuryOptions old, SettingsRequest r) => new()
        {
            Enabled = r.Enabled,
            Sandbox = r.Sandbox,
            ApiKey = string.IsNullOrWhiteSpace(r.ApiKey) ? old.ApiKey : r.ApiKey.Trim(),
            Login = (r.Login ?? "").Trim(),
            Password = string.IsNullOrEmpty(r.Password) ? old.Password : r.Password,
            IssuerId = (r.IssuerId ?? "").Trim(),
            InitiatorLogin = (r.InitiatorLogin ?? "").Trim(),
            EnterpriseGuid = (r.EnterpriseGuid ?? "").Trim(),
            AutoSyncMinutes = r.AutoSyncMinutes,
            InitialDays = r.InitialDays,
            LastSyncAt = old.LastSyncAt,
            LastError = old.LastError,
            SyncFrom = old.SyncFrom
        };
    }
}
