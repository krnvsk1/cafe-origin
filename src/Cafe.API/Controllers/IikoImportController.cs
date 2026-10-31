using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Authorization;
using Cafe.API.Data;
using Cafe.API.Extensions;
using Cafe.API.Models;
using Cafe.API.Services.Iiko;

namespace Cafe.API.Controllers
{
    /// <summary>Импорт технологических карт из iiko (iikoServer в локальной сети)</summary>
    [ApiController]
    [Route("api/iiko")]
    [HasPermission(Permissions.TechCards)]
    public class IikoImportController : ControllerBase
    {
        private const string SettingsKey = "iiko";
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly ApplicationDbContext _context;
        private readonly IikoImportService _import;

        public IikoImportController(ApplicationDbContext context, IikoImportService import)
        {
            _context = context;
            _import = import;
        }

        public class ConnectRequest
        {
            public string Url { get; set; } = string.Empty;
            public string Login { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        public class ImportRequest
        {
            public Guid SnapshotId { get; set; }
            public bool Overwrite { get; set; }
            public List<ChoiceDto> Dishes { get; set; } = new();
        }

        public class ChoiceDto
        {
            public string IikoId { get; set; } = string.Empty;
            /// <summary>dish или addon</summary>
            public string Kind { get; set; } = "dish";
            public int Id { get; set; }
        }

        private record Saved(string Url, string Login);

        // GET: api/iiko/connection — сохранённые адрес и логин (пароль не храним)
        [HttpGet("connection")]
        public async Task<IActionResult> Connection()
        {
            var row = await _context.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SettingsKey);
            var saved = row == null ? null : JsonSerializer.Deserialize<Saved>(row.Value, Json);
            return Ok(new { url = saved?.Url ?? "", login = saved?.Login ?? "" });
        }

        // POST: api/iiko/preview — войти в iiko, скачать техкарты и показать, что с чем совпало
        [HttpPost("preview")]
        public async Task<IActionResult> Preview([FromBody] ConnectRequest r)
        {
            try
            {
                var preview = await _import.PreviewAsync(r.Url, r.Login, r.Password, HttpContext.RequestAborted);
                await SaveConnectionAsync(IikoClient.NormalizeBaseUrl(r.Url), r.Login.Trim());
                return Ok(preview);
            }
            catch (IikoException ex) { return BadRequest(new { message = ex.Message }); }
        }

        // POST: api/iiko/import — перенести выбранные техкарты
        [HttpPost("import")]
        public async Task<IActionResult> Import([FromBody] ImportRequest r)
        {
            if (r.Dishes.Count == 0) return BadRequest(new { message = "Выберите хотя бы одно блюдо" });
            try
            {
                var result = await _import.ImportAsync(r.SnapshotId,
                    r.Dishes.Select(d => new IikoImportService.ImportChoice(d.IikoId, d.Kind, d.Id)).ToList(), r.Overwrite);
                return Ok(result);
            }
            catch (IikoException ex) { return BadRequest(new { message = ex.Message }); }
        }

        private async Task SaveConnectionAsync(string url, string login)
        {
            var row = await _context.AppSettings.FirstOrDefaultAsync(s => s.Key == SettingsKey);
            if (row == null)
            {
                row = new AppSetting { Key = SettingsKey };
                _context.AppSettings.Add(row);
            }
            row.Value = JsonSerializer.Serialize(new Saved(url, login), Json);
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedByUserId = User.GetUserId();
            await _context.SaveChangesAsync();
        }
    }
}
