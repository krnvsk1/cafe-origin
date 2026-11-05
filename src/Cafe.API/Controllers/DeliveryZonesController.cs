using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Models;
using Cafe.API.Authorization;
using Cafe.API.Extensions;

namespace Cafe.API.Controllers
{
    /// <summary>
    /// Зоны доставки: публичный список, проверка адреса, управление в админке
    /// </summary>
    [ApiController]
    [Route("api/delivery-zones")]
    public class DeliveryZonesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public DeliveryZonesController(ApplicationDbContext context)
        {
            _context = context;
        }

        public class ZoneRequest
        {
            public string Name { get; set; } = string.Empty;
            public double[][] Polygon { get; set; } = Array.Empty<double[]>();
            public decimal DeliveryPrice { get; set; }
            public decimal MinOrderAmount { get; set; }
            public decimal? FreeDeliveryFrom { get; set; }
            public int DeliveryMinutes { get; set; } = 45;
            public string? Color { get; set; }
            public bool IsActive { get; set; } = true;
            public int SortOrder { get; set; }
        }

        public class CheckRequest
        {
            public double Latitude { get; set; }
            public double Longitude { get; set; }
            public decimal Subtotal { get; set; }
        }

        public const string RulesKey = "delivery_rules";
        private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

        /// <summary>Общие правила доставки: минимальная сумма заказа для любого адреса (у зоны может быть своя, больше)</summary>
        public class DeliveryRules
        {
            public decimal MinOrderAmount { get; set; }
        }

        public static async Task<DeliveryRules> RulesAsync(ApplicationDbContext context)
        {
            var row = await context.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == RulesKey);
            return row == null ? new DeliveryRules() : System.Text.Json.JsonSerializer.Deserialize<DeliveryRules>(row.Value, Json) ?? new DeliveryRules();
        }

        /// <summary>Сколько минимум для доставки: больше из общего минимума и минимума зоны</summary>
        public static decimal MinFor(DeliveryRules rules, DeliveryZone? zone) => Math.Max(rules.MinOrderAmount, zone?.MinOrderAmount ?? 0);

        // GET: api/delivery-zones/rules — общая минимальная сумма заказа на доставку (для сайта)
        [HttpGet("rules")]
        public async Task<IActionResult> GetRules() => Ok(await RulesAsync(_context));

        // PUT: api/delivery-zones/rules
        [HttpPut("rules")]
        [HasPermission(Permissions.Zones)]
        public async Task<IActionResult> SaveRules([FromBody] DeliveryRules r)
        {
            if (r.MinOrderAmount < 0 || r.MinOrderAmount > 1_000_000) return BadRequest(new { message = "Проверьте сумму" });
            var row = await _context.AppSettings.FirstOrDefaultAsync(s => s.Key == RulesKey);
            if (row == null) { row = new AppSetting { Key = RulesKey }; _context.AppSettings.Add(row); }
            row.Value = System.Text.Json.JsonSerializer.Serialize(new DeliveryRules { MinOrderAmount = Math.Round(r.MinOrderAmount, 2) }, Json);
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedByUserId = User.GetUserId();
            await _context.SaveChangesAsync();
            return Ok(await RulesAsync(_context));
        }

        // GET: api/delivery-zones — активные зоны (для карты у клиента)
        [HttpGet]
        public async Task<IActionResult> GetActive()
        {
            var zones = await _context.DeliveryZones.AsNoTracking()
                .Where(z => z.IsActive)
                .OrderBy(z => z.SortOrder).ThenBy(z => z.Id)
                .ToListAsync();
            return Ok(zones);
        }

        // GET: api/delivery-zones/all — все зоны (админка)
        [HttpGet("all")]
        [HasPermission(Permissions.Zones)]
        public async Task<IActionResult> GetAll()
        {
            var zones = await _context.DeliveryZones.AsNoTracking()
                .OrderBy(z => z.SortOrder).ThenBy(z => z.Id)
                .ToListAsync();
            return Ok(zones);
        }

        // POST: api/delivery-zones/check — в какую зону попадает точка и сколько стоит доставка
        [HttpPost("check")]
        public async Task<IActionResult> Check([FromBody] CheckRequest request)
        {
            var zones = await _context.DeliveryZones.AsNoTracking()
                .Where(z => z.IsActive)
                .OrderBy(z => z.SortOrder).ThenBy(z => z.Id)
                .ToListAsync();

            var zone = zones.FirstOrDefault(z => z.Contains(request.Latitude, request.Longitude));
            var rules = await RulesAsync(_context);
            if (zone == null)
                return Ok(new { Deliverable = false, Message = "К сожалению, по этому адресу мы пока не доставляем" });

            return Ok(new
            {
                Deliverable = true,
                ZoneId = zone.Id,
                ZoneName = zone.Name,
                DeliveryPrice = zone.PriceFor(request.Subtotal),
                MinOrderAmount = MinFor(rules, zone),
                zone.FreeDeliveryFrom,
                zone.DeliveryMinutes,
                MinOrderReached = request.Subtotal >= MinFor(rules, zone)
            });
        }

        // POST: api/delivery-zones
        [HttpPost]
        [HasPermission(Permissions.Zones)]
        public async Task<IActionResult> Create([FromBody] ZoneRequest request)
        {
            var error = Validate(request);
            if (error != null) return BadRequest(new { Message = error });

            var zone = new DeliveryZone { CreatedAt = DateTime.UtcNow };
            Apply(zone, request);
            _context.DeliveryZones.Add(zone);
            await _context.SaveChangesAsync();
            return Ok(zone);
        }

        // PUT: api/delivery-zones/{id}
        [HttpPut("{id}")]
        [HasPermission(Permissions.Zones)]
        public async Task<IActionResult> Update(int id, [FromBody] ZoneRequest request)
        {
            var zone = await _context.DeliveryZones.FindAsync(id);
            if (zone == null) return NotFound(new { Message = "Зона не найдена" });

            var error = Validate(request);
            if (error != null) return BadRequest(new { Message = error });

            Apply(zone, request);
            await _context.SaveChangesAsync();
            return Ok(zone);
        }

        // DELETE: api/delivery-zones/{id}
        [HttpDelete("{id}")]
        [HasPermission(Permissions.Zones)]
        public async Task<IActionResult> Delete(int id)
        {
            var deleted = await _context.DeliveryZones.Where(z => z.Id == id).ExecuteDeleteAsync();
            return deleted > 0 ? Ok(new { Id = id }) : NotFound(new { Message = "Зона не найдена" });
        }

        private static string? Validate(ZoneRequest r)
        {
            if (string.IsNullOrWhiteSpace(r.Name)) return "Укажите название зоны";
            if (r.Polygon == null || r.Polygon.Length < 3 || r.Polygon.Any(p => p == null || p.Length < 2))
                return "Нарисуйте зону на карте (минимум 3 точки)";
            if (r.Polygon.Any(p => p[0] < -90 || p[0] > 90 || p[1] < -180 || p[1] > 180))
                return "Некорректные координаты зоны";
            if (r.DeliveryPrice < 0 || r.MinOrderAmount < 0 || r.FreeDeliveryFrom < 0) return "Суммы не могут быть отрицательными";
            if (r.DeliveryMinutes <= 0 || r.DeliveryMinutes > 600) return "Укажите время доставки в минутах";
            return null;
        }

        private static void Apply(DeliveryZone zone, ZoneRequest r)
        {
            zone.Name = r.Name.Trim();
            zone.Polygon = r.Polygon.Select(p => new[] { p[0], p[1] }).ToArray();
            zone.DeliveryPrice = r.DeliveryPrice;
            zone.MinOrderAmount = r.MinOrderAmount;
            zone.FreeDeliveryFrom = r.FreeDeliveryFrom;
            zone.DeliveryMinutes = r.DeliveryMinutes;
            zone.Color = string.IsNullOrWhiteSpace(r.Color) ? "#0891b2" : r.Color.Trim();
            zone.IsActive = r.IsActive;
            zone.SortOrder = r.SortOrder;
        }
    }
}
