using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Authorization;
using Kumashaurma.API.Data;
using Kumashaurma.API.Extensions;
using Kumashaurma.API.Services.Chz;

namespace Kumashaurma.API.Controllers
{
    /// <summary>
    /// Честный знак: вход по КЭП (подпись в браузере), вывод из оборота маркированного товара, история документов
    /// </summary>
    [ApiController]
    [Route("api/chz")]
    [HasPermission(Permissions.Chz)]
    public class ChzController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ChzService _chz;
        private readonly ChzSettingsProvider _settings;

        public ChzController(ApplicationDbContext context, ChzService chz, ChzSettingsProvider settings)
        {
            _context = context;
            _chz = chz;
            _settings = settings;
        }

        public class SettingsRequest
        {
            public bool Enabled { get; set; }
            public bool Sandbox { get; set; }
            public string Inn { get; set; } = string.Empty;
            public List<string> ProductGroups { get; set; } = new();
        }

        public class SignInRequest
        {
            public string Uuid { get; set; } = string.Empty;
            public string Signature { get; set; } = string.Empty;
        }

        public class WithdrawalRequest
        {
            public string ProductGroup { get; set; } = "milk";
            public string Action { get; set; } = "PRODUCTION_USE";
            public DateTime? Date { get; set; }
            /// <summary>Коды маркировки, по одному в строке (как с ручного сканера)</summary>
            public string Codes { get; set; } = string.Empty;
            public string? Comment { get; set; }
            public int? StockDocumentId { get; set; }
        }

        public class SubmitRequest
        {
            public string Signature { get; set; } = string.Empty;
        }

        // GET: api/chz/status — настройки, справочники, есть ли действующий вход
        [HttpGet("status")]
        public async Task<IActionResult> Status()
        {
            var o = await _settings.GetAsync();
            var (signedIn, expires) = await _chz.SessionAsync();
            return Ok(new
            {
                o.Enabled,
                o.Sandbox,
                o.Inn,
                o.ProductGroups,
                signedIn,
                expiresAt = expires,
                actions = ChzService.Actions.Select(a => new { key = a.Key, title = a.Value }),
                groups = ChzService.Groups.Select(g => new { key = g.Key, title = g.Value })
            });
        }

        // PUT: api/chz/settings
        [HttpPut("settings")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> SaveSettings([FromBody] SettingsRequest r)
        {
            var inn = (r.Inn ?? "").Trim();
            if (r.Enabled && !System.Text.RegularExpressions.Regex.IsMatch(inn, @"^\d{10}(\d{2})?$"))
                return BadRequest(new { message = "ИНН — 10 цифр для организации или 12 для ИП" });
            var groups = r.ProductGroups.Where(ChzService.Groups.ContainsKey).Distinct().ToList();
            if (r.Enabled && groups.Count == 0) return BadRequest(new { message = "Выберите хотя бы одну товарную группу" });

            var old = await _settings.GetAsync();
            if (old.Inn != inn || old.Sandbox != r.Sandbox) _chz.SignOut(old);
            await _settings.SaveAsync(new ChzOptions { Enabled = r.Enabled, Sandbox = r.Sandbox, Inn = inn, ProductGroups = groups }, User.GetUserId());
            return await Status();
        }

        // POST: api/chz/auth/start — строка для подписи КЭП
        [HttpPost("auth/start")]
        public async Task<IActionResult> AuthStart()
        {
            try
            {
                var key = await _chz.StartSignInAsync(HttpContext.RequestAborted);
                return Ok(new { key.Uuid, key.Data });
            }
            catch (ChzApiException ex) { return ChzError(ex); }
            catch (HttpRequestException ex) { return StatusCode(502, new { message = $"Честный знак недоступен: {ex.Message}" }); }
        }

        // POST: api/chz/auth/complete — подписанная строка → вход
        [HttpPost("auth/complete")]
        public async Task<IActionResult> AuthComplete([FromBody] SignInRequest r)
        {
            if (string.IsNullOrWhiteSpace(r.Uuid) || string.IsNullOrWhiteSpace(r.Signature))
                return BadRequest(new { message = "Нет подписи" });
            try
            {
                var expires = await _chz.CompleteSignInAsync(r.Uuid, r.Signature, HttpContext.RequestAborted);
                return Ok(new { signedIn = true, expiresAt = expires });
            }
            catch (ChzApiException ex) { return ChzError(ex); }
            catch (HttpRequestException ex) { return StatusCode(502, new { message = $"Честный знак недоступен: {ex.Message}" }); }
        }

        // POST: api/chz/auth/logout
        [HttpPost("auth/logout")]
        public async Task<IActionResult> Logout()
        {
            _chz.SignOut(await _settings.GetAsync());
            return NoContent();
        }

        // GET: api/chz/documents
        [HttpGet("documents")]
        public async Task<IActionResult> Documents([FromQuery] int take = 50)
        {
            var docs = await _context.ChzDocuments.AsNoTracking()
                .OrderByDescending(d => d.Id)
                .Take(Math.Clamp(take, 1, 200))
                .Select(d => new
                {
                    d.Id, d.ProductGroup, d.Action, d.ActionDate, d.DocumentNumber, d.CodesCount, d.Status,
                    d.ChzDocumentId, d.Result, d.Comment, d.CreatedAt, d.SentAt, d.CheckedAt, d.StockDocumentId
                })
                .ToListAsync();
            return Ok(docs);
        }

        // GET: api/chz/documents/{id}
        [HttpGet("documents/{id:int}")]
        public async Task<IActionResult> Document(int id)
        {
            var d = await _context.ChzDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (d == null) return NotFound(new { message = "Документ не найден" });
            return Ok(new
            {
                d.Id, d.ProductGroup, d.Action, d.ActionDate, d.DocumentNumber, d.CodesCount, d.Status, d.ChzDocumentId,
                d.Result, d.Comment, d.CreatedAt, d.SentAt, d.CheckedAt, d.StockDocumentId,
                codes = d.Codes.Split('\n', StringSplitOptions.RemoveEmptyEntries),
                // Это подписывается в браузере (откреплённая подпись)
                productDocument = d.Status is "draft" or "error" ? d.ProductDocument : null
            });
        }

        // GET: api/chz/balance?group=milk&days=365 — коды «в обороте» на нашем балансе
        [HttpGet("balance")]
        public async Task<IActionResult> Balance([FromQuery] string group = "milk", [FromQuery] int days = 365)
        {
            try
            {
                var (codes, truncated) = await _chz.BalanceAsync(group, days, HttpContext.RequestAborted);
                var items = codes.GroupBy(c => c.Gtin)
                    .Select(g => new
                    {
                        gtin = g.Key,
                        count = g.Count(),
                        lastEmission = g.Max(x => x.EmissionDate),
                        codes = g.Select(x => x.Cis).ToList()
                    })
                    .OrderByDescending(x => x.lastEmission)
                    .ToList();
                return Ok(new { total = codes.Count, truncated, items });
            }
            catch (ChzApiException ex) { return ChzError(ex); }
            catch (HttpRequestException ex) { return StatusCode(502, new { message = $"Честный знак недоступен: {ex.Message}" }); }
        }

        // POST: api/chz/withdrawals — собрать документ вывода из оборота (черновик для подписи)
        [HttpPost("withdrawals")]
        public async Task<IActionResult> Prepare([FromBody] WithdrawalRequest r)
        {
            try
            {
                var doc = await _chz.PrepareWithdrawalAsync(r.ProductGroup, r.Action, r.Date ?? DateTime.UtcNow.AddHours(3), r.Codes,
                    r.Comment, r.StockDocumentId, User.GetUserId());
                return await Document(doc.Id);
            }
            catch (ChzApiException ex) { return ChzError(ex); }
        }

        // POST: api/chz/withdrawals/{id}/submit — отправить подписанный документ
        [HttpPost("withdrawals/{id:int}/submit")]
        public async Task<IActionResult> Submit(int id, [FromBody] SubmitRequest r)
        {
            if (string.IsNullOrWhiteSpace(r.Signature)) return BadRequest(new { message = "Нет подписи" });
            try
            {
                await _chz.SubmitAsync(id, r.Signature, HttpContext.RequestAborted);
                return await Document(id);
            }
            catch (ChzApiException ex) { return ChzError(ex); }
            catch (HttpRequestException ex) { return StatusCode(502, new { message = $"Честный знак недоступен: {ex.Message}" }); }
        }

        // POST: api/chz/withdrawals/{id}/refresh — узнать результат проверки
        [HttpPost("withdrawals/{id:int}/refresh")]
        public async Task<IActionResult> Refresh(int id)
        {
            try
            {
                await _chz.RefreshAsync(id, HttpContext.RequestAborted);
                return await Document(id);
            }
            catch (ChzApiException ex) { return ChzError(ex); }
            catch (HttpRequestException ex) { return StatusCode(502, new { message = $"Честный знак недоступен: {ex.Message}" }); }
        }

        // DELETE: api/chz/documents/{id} — удалить неотправленный черновик
        [HttpDelete("documents/{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var d = await _context.ChzDocuments.FirstOrDefaultAsync(x => x.Id == id);
            if (d == null) return NotFound();
            if (d.Status is not ("draft" or "error")) return BadRequest(new { message = "Отправленный документ удалить нельзя" });
            _context.ChzDocuments.Remove(d);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        private IActionResult ChzError(ChzApiException ex) =>
            StatusCode(ex.StatusCode == 401 ? 409 : ex.StatusCode is >= 400 and < 500 ? 400 : 502,
                new { message = ex.Message, needSignIn = ex.StatusCode == 401 });
    }
}
