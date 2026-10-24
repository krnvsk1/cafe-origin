using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;

namespace Kumashaurma.API.Controllers
{
    /// <summary>
    /// Стоп-лист: быстрое включение/выключение позиций меню и добавок.
    /// </summary>
    [ApiController]
    [Route("api/stoplist")]
    [Authorize(Roles = "admin,manager")]
    public class StopListController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<StopListController> _logger;

        public StopListController(ApplicationDbContext context, ILogger<StopListController> logger)
        {
            _context = context;
            _logger = logger;
        }

        public class SetAvailabilityRequest
        {
            public bool IsAvailable { get; set; }
        }

        // GET: api/stoplist — все позиции и добавки, включая недоступные
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var items = await _context.Shawarmas
                .AsNoTracking()
                .Include(s => s.Parent)
                .OrderBy(s => s.Parent != null ? s.Parent.SortOrder : s.SortOrder)
                .ThenBy(s => s.SortOrder)
                .Select(s => new
                {
                    s.Id,
                    s.Name,
                    s.Price,
                    s.IsAvailable,
                    s.ParentId,
                    ParentName = s.Parent != null ? s.Parent.Name : null,
                    HasChildren = s.Children.Any()
                })
                .ToListAsync();

            var addons = await _context.Addons
                .AsNoTracking()
                .Include(a => a.Category)
                .OrderBy(a => a.Category.Name)
                .ThenBy(a => a.DisplayOrder)
                .ThenBy(a => a.Name)
                .Select(a => new
                {
                    a.Id,
                    a.Name,
                    a.Price,
                    a.IsAvailable,
                    CategoryName = a.Category.Name
                })
                .ToListAsync();

            return Ok(new { Items = items, Addons = addons });
        }

        // PATCH: api/stoplist/items/{id}
        [HttpPatch("items/{id}")]
        public async Task<IActionResult> SetItemAvailability(int id, [FromBody] SetAvailabilityRequest request)
        {
            var item = await _context.Shawarmas.FindAsync(id);
            if (item == null)
                return NotFound(new { Message = $"Позиция с ID {id} не найдена" });

            item.IsAvailable = request.IsAvailable;
            item.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("⛔ Стоп-лист: позиция {Name} (ID {Id}) → {State}",
                item.Name, item.Id, request.IsAvailable ? "в продаже" : "в стопе");

            return Ok(new { item.Id, item.IsAvailable });
        }

        // PATCH: api/stoplist/addons/{id}
        [HttpPatch("addons/{id}")]
        public async Task<IActionResult> SetAddonAvailability(int id, [FromBody] SetAvailabilityRequest request)
        {
            var addon = await _context.Addons.FindAsync(id);
            if (addon == null)
                return NotFound(new { Message = $"Добавка с ID {id} не найдена" });

            addon.IsAvailable = request.IsAvailable;
            addon.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("⛔ Стоп-лист: добавка {Name} (ID {Id}) → {State}",
                addon.Name, addon.Id, request.IsAvailable ? "в продаже" : "в стопе");

            return Ok(new { addon.Id, addon.IsAvailable });
        }
    }
}
