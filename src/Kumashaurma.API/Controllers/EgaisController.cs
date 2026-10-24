using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Authorization;
using Kumashaurma.API.Data;
using Kumashaurma.API.Extensions;
using Kumashaurma.API.Models;
using Kumashaurma.API.Services;
using Kumashaurma.API.Services.Egais;

namespace Kumashaurma.API.Controllers
{
    /// <summary>
    /// ЕГАИС: входящие накладные на алкоголь (пиво) через УТМ, сопоставление со складом, акты приёмки
    /// </summary>
    [ApiController]
    [Route("api/egais")]
    [HasPermission(Permissions.InventoryView)]
    public class EgaisController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly EgaisService _egais;
        private readonly UtmClient _utm;
        private readonly EgaisSettingsProvider _settings;

        public EgaisController(ApplicationDbContext context, EgaisService egais, UtmClient utm, EgaisSettingsProvider settings)
        {
            _context = context;
            _egais = egais;
            _utm = utm;
            _settings = settings;
        }

        private EgaisOptions _options => _settings.Current;

        public class SettingsRequest
        {
            public bool Enabled { get; set; }
            public string UtmUrl { get; set; } = "http://localhost:8080";
            public string? FsrarId { get; set; }
            public int AutoSyncMinutes { get; set; } = 10;
        }

        public class TestRequest
        {
            public string UtmUrl { get; set; } = string.Empty;
        }

        // GET: api/egais/settings
        [HttpGet("settings")]
        [HasPermission(Permissions.Settings)]
        public IActionResult GetSettings() => Ok(new
        {
            _options.Enabled,
            _options.UtmUrl,
            _options.FsrarId,
            _options.AutoSyncMinutes,
            savedInAdmin = _settings.FromDatabase,
            updatedAt = _settings.UpdatedAt
        });

        // PUT: api/egais/settings — сохранить; действует сразу
        [HttpPut("settings")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> SaveSettings([FromBody] SettingsRequest request)
        {
            var options = new EgaisOptions
            {
                Enabled = request.Enabled,
                UtmUrl = (request.UtmUrl ?? "").Trim().TrimEnd('/'),
                FsrarId = (request.FsrarId ?? "").Trim(),
                AutoSyncMinutes = request.AutoSyncMinutes
            };
            var error = EgaisSettingsProvider.Validate(options);
            if (error != null) return BadRequest(new { message = error });

            await _settings.SaveAsync(options, User.GetUserId());
            return GetSettings();
        }

        // POST: api/egais/settings/test — проверить связь с УТМ по введённому адресу (без сохранения)
        [HttpPost("settings/test")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> Test([FromBody] TestRequest request)
        {
            var probe = new EgaisOptions { UtmUrl = request.UtmUrl ?? "" };
            var error = EgaisSettingsProvider.Validate(probe);
            if (error != null) return BadRequest(new { message = error });

            var (ok, message) = await _utm.PingAsync(probe.UtmUrl, HttpContext.RequestAborted);
            return Ok(new { ok, message = ok ? "УТМ отвечает" : message });
        }

        public class MappingRequest
        {
            public List<MappingLine> Lines { get; set; } = new();
        }

        public class MappingLine
        {
            public int LineId { get; set; }
            public int? StockItemId { get; set; }
        }

        public class ActRequest
        {
            /// <summary>accept | reject | differences</summary>
            public string Decision { get; set; } = "accept";
            public string? Note { get; set; }
            public List<ActLineRequest> Lines { get; set; } = new();
        }

        public class ActLineRequest
        {
            public int LineId { get; set; }
            public decimal? RealQuantity { get; set; }
        }

        // GET: api/egais/status — настроен ли ЕГАИС и доступен ли УТМ
        [HttpGet("status")]
        public async Task<IActionResult> Status()
        {
            var reachable = _options.Enabled && (await _utm.PingAsync(null, HttpContext.RequestAborted)).Ok;
            var counts = await _context.EgaisDocuments.AsNoTracking()
                .GroupBy(d => d.Status)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);

            return Ok(new
            {
                enabled = _options.Enabled,
                configured = _options.Enabled && !string.IsNullOrWhiteSpace(_options.FsrarId),
                utmUrl = _options.UtmUrl,
                fsrarId = string.IsNullOrWhiteSpace(_options.FsrarId) ? null : _options.FsrarId,
                reachable,
                autoSyncMinutes = _options.AutoSyncMinutes,
                lastSyncAt = EgaisService.LastSyncAt,
                lastSyncError = EgaisService.LastSyncError,
                toProcess = counts.GetValueOrDefault(EgaisStatuses.Ready) + counts.GetValueOrDefault(EgaisStatuses.Error),
                awaitingRegId = counts.GetValueOrDefault(EgaisStatuses.AwaitingRegId),
                awaitingTicket = counts.GetValueOrDefault(EgaisStatuses.ActSent)
            });
        }

        // POST: api/egais/sync — забрать входящие из УТМ
        [HttpPost("sync")]
        [HasPermission(Permissions.InventoryManage)]
        public async Task<IActionResult> Sync()
        {
            if (!_options.Enabled) return BadRequest(new { message = "ЕГАИС выключен — включите его в настройках" });
            try
            {
                var r = await _egais.SyncAsync(HttpContext.RequestAborted);
                return Ok(new { wayBills = r.WayBills, regInfos = r.RegInfos, tickets = r.Tickets, skipped = r.Skipped, errors = r.Errors });
            }
            catch (EgaisException ex)
            {
                return StatusCode(502, new { message = ex.Message });
            }
        }

        // GET: api/egais/documents?status=
        [HttpGet("documents")]
        public async Task<IActionResult> List([FromQuery] string? status)
        {
            var q = _context.EgaisDocuments.AsNoTracking().AsQueryable();
            if (status == "active")
                q = q.Where(d => d.Status == EgaisStatuses.AwaitingRegId || d.Status == EgaisStatuses.Ready
                                 || d.Status == EgaisStatuses.ActSent || d.Status == EgaisStatuses.Error);
            else if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(d => d.Status == status);

            var docs = await q.OrderByDescending(d => d.Date).ThenByDescending(d => d.Id).Take(200)
                .Select(d => new
                {
                    d.Id, d.Number, d.Date, d.ShipperName, d.ShipperInn, d.WbRegId, d.Status, d.ActDecision,
                    d.ActSentAt, d.ResultComment, d.StockDocumentId, d.CreatedAt,
                    linesCount = d.Lines.Count,
                    unmapped = d.Lines.Count(l => l.StockItemId == null),
                    total = d.Lines.Sum(l => l.Quantity * l.Price)
                })
                .ToListAsync();
            return Ok(docs);
        }

        // GET: api/egais/documents/{id}
        [HttpGet("documents/{id:int}")]
        public async Task<IActionResult> Get(int id)
        {
            var d = await _context.EgaisDocuments.AsNoTracking()
                .Include(x => x.Lines).ThenInclude(l => l.StockItem)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (d == null) return NotFound(new { message = "Накладная не найдена" });

            return Ok(new
            {
                d.Id, d.Number, d.Date, d.ShipperName, d.ShipperInn, d.ShipperFsrarId, d.WbRegId, d.Status,
                d.ActNumber, d.ActDecision, d.ActNote, d.ActSentAt, d.ResultComment, d.ResultAt, d.StockDocumentId,
                lines = d.Lines.OrderBy(l => l.Id).Select(l => new
                {
                    l.Id, l.Identity, l.ProductName, l.AlcCode, l.Capacity, l.AlcVolume, l.UnitType,
                    l.Quantity, l.Price, l.InformF2RegId, l.RealQuantity, l.StockItemId,
                    stockItemName = l.StockItem?.Name,
                    stockUnit = l.StockItem?.Unit,
                    // Сколько придёт на склад в его единицах
                    stockQuantity = l.StockItem != null ? TryConvert(l, l.StockItem, l.RealQuantity ?? l.Quantity) : null,
                    conversionError = l.StockItem != null ? ConversionError(l, l.StockItem) : null
                })
            });
        }

        // PUT: api/egais/documents/{id}/mapping — привязать строки к позициям склада
        [HttpPut("documents/{id:int}/mapping")]
        [HasPermission(Permissions.InventoryManage)]
        public async Task<IActionResult> Mapping(int id, [FromBody] MappingRequest request)
        {
            try
            {
                await _egais.SaveMappingAsync(id, request.Lines.Select(l => (l.LineId, l.StockItemId)));
                return await Get(id);
            }
            catch (EgaisException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // POST: api/egais/documents/{id}/act — принять / отказать / принять с расхождениями
        [HttpPost("documents/{id:int}/act")]
        [HasPermission(Permissions.InventoryManage)]
        public async Task<IActionResult> Act(int id, [FromBody] ActRequest request)
        {
            var decision = request.Decision?.ToLowerInvariant() switch
            {
                "accept" => ActDecision.Accepted,
                "reject" => ActDecision.Rejected,
                "differences" => ActDecision.Differences,
                _ => (ActDecision?)null
            };
            if (decision == null) return BadRequest(new { message = "Неизвестное решение" });
            if (decision == ActDecision.Rejected && string.IsNullOrWhiteSpace(request.Note))
                return BadRequest(new { message = "Укажите причину отказа" });

            try
            {
                await _egais.SendActAsync(id, decision.Value, request.Note,
                    request.Lines.Select(l => new ActLine(l.LineId, l.RealQuantity)).ToList(), User.GetUserId());
                return await Get(id);
            }
            catch (EgaisException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InventoryException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (HttpRequestException)
            {
                return StatusCode(502, new { message = $"УТМ недоступен ({_options.UtmUrl})" });
            }
        }

        private static decimal? TryConvert(EgaisDocumentLine l, StockItem item, decimal qty)
        {
            try { return Math.Round(EgaisService.ConvertToStockUnits(l, item, qty), 3); }
            catch (EgaisException) { return null; }
        }

        private static string? ConversionError(EgaisDocumentLine l, StockItem item)
        {
            try { EgaisService.ConvertToStockUnits(l, item, 1); return null; }
            catch (EgaisException ex) { return ex.Message; }
        }
    }
}
