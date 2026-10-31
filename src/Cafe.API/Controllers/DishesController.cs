using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Models;
using Cafe.API.Authorization;

namespace Cafe.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DishesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public DishesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/dish
        // Возвращает все позиции с дочерними (для клиентского меню)
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Dish>>> GetAll()
        {
            var dishes = await _context.Dishes
                .Include(s => s.Children)
                    .ThenInclude(c => c.Images)
                .Include(s => s.Children)
                    .ThenInclude(c => c.Addons)
                        .ThenInclude(a => a.Addon)
                            .ThenInclude(a => a.Category)
                .Include(s => s.Images)
                .Include(s => s.Addons)
                    .ThenInclude(a => a.Addon)
                        .ThenInclude(a => a.Category)
                .OrderBy(s => s.SortOrder)
                .ToListAsync();

            return Ok(dishes);
        }

        // GET: api/dish/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<Dish>> GetById(int id)
        {
            var dish = await _context.Dishes
                .Include(s => s.Children)
                    .ThenInclude(c => c.Images)
                .Include(s => s.Children)
                    .ThenInclude(c => c.Addons)
                        .ThenInclude(a => a.Addon)
                            .ThenInclude(a => a.Category)
                .Include(s => s.Images)
                .Include(s => s.Addons)
                    .ThenInclude(a => a.Addon)
                        .ThenInclude(a => a.Category)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (dish == null)
                return NotFound(new { Message = $"Шаурма с ID {id} не найдена" });

            return Ok(dish);
        }

        // GET: api/dish/sections — разделы меню (поле Category у карточек)
        [HttpGet("sections")]
        public async Task<ActionResult<IEnumerable<string>>> GetSections()
        {
            var sections = await _context.Dishes
                .Where(s => s.ParentId == null && s.Category != "")
                .GroupBy(s => s.Category)
                .Select(g => new { Name = g.Key, Order = g.Min(x => x.SortOrder) })
                .OrderBy(x => x.Order)
                .Select(x => x.Name)
                .ToListAsync();
            return Ok(sections);
        }

        // GET: api/dish/categories
        [HttpGet("categories")]
        public async Task<ActionResult<IEnumerable<string>>> GetCategories()
        {
            var categories = await _context.Dishes
                .Where(s => s.ParentId == null)
                .Select(s => s.Name)
                .OrderBy(n => n)
                .ToListAsync();

            return Ok(categories);
        }

        // POST: api/dish
        [HttpPost]
        [HasPermission(Permissions.Menu)]
        public async Task<ActionResult<Dish>> Create([FromBody] CreateDishDto dto)
        {
            // Название уникально в пределах родителя: у разных карточек могут быть варианты «Большая»
            if (await _context.Dishes.AnyAsync(s => s.Name == dto.Name && s.ParentId == dto.ParentId))
                return BadRequest(new { Message = "Позиция с таким названием уже есть" });

            var dish = new Dish
            {
                Name = dto.Name,
                Price = dto.Price,
                Description = dto.Description ?? string.Empty,
                IsSpicy = dto.IsSpicy,
                IsAvailable = dto.IsAvailable,
                IsPromo = dto.IsPromo,
                ParentId = dto.ParentId,
                SortOrder = dto.SortOrder,
                Category = dto.Category?.Trim() ?? string.Empty,
                Badge = string.IsNullOrWhiteSpace(dto.Badge) ? null : dto.Badge.Trim(),
                OrderTypes = SalesChannels.Format(dto.AvailableOrderTypes),
                KitchenStationId = dto.KitchenStationId is > 0 ? dto.KitchenStationId : null
            };

            _context.Dishes.Add(dish);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = dish.Id }, dish);
        }

        // PUT: api/dish/{id}
        [HttpPut("{id}")]
        [HasPermission(Permissions.Menu)]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateDishDto dto)
        {
            var dish = await _context.Dishes.FindAsync(id);
            if (dish == null)
                return NotFound(new { Message = $"Шаурма с ID {id} не найдена" });

            var parentId = dto.ParentId ?? dish.ParentId;
            if (dto.Name != null && dto.Name != dish.Name &&
                await _context.Dishes.AnyAsync(s => s.Name == dto.Name && s.ParentId == parentId && s.Id != id))
                return BadRequest(new { Message = "Позиция с таким названием уже есть" });

            if (dto.Name != null) dish.Name = dto.Name;
            if (dto.Price.HasValue) dish.Price = dto.Price.Value;
            if (dto.Description != null) dish.Description = dto.Description;
            if (dto.IsSpicy.HasValue) dish.IsSpicy = dto.IsSpicy.Value;
            if (dto.IsAvailable.HasValue) dish.IsAvailable = dto.IsAvailable.Value;
            if (dto.IsPromo.HasValue) dish.IsPromo = dto.IsPromo.Value;
            if (dto.ParentId.HasValue) dish.ParentId = dto.ParentId;
            if (dto.SortOrder.HasValue) dish.SortOrder = dto.SortOrder.Value;
            if (dto.Category != null) dish.Category = dto.Category.Trim();
            if (dto.Badge != null) dish.Badge = string.IsNullOrWhiteSpace(dto.Badge) ? null : dto.Badge.Trim();
            // Пустой список или все способы — без ограничений
            if (dto.AvailableOrderTypes != null) dish.OrderTypes = SalesChannels.Format(dto.AvailableOrderTypes);
            // 0 — снять метку (вариант пойдёт на кухню карточки)
            if (dto.KitchenStationId.HasValue)
            {
                if (dto.KitchenStationId > 0 && !await _context.KitchenStations.AnyAsync(k => k.Id == dto.KitchenStationId))
                    return BadRequest(new { Message = "Кухня не найдена" });
                dish.KitchenStationId = dto.KitchenStationId > 0 ? dto.KitchenStationId : null;
            }

            dish.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(dish);
        }

        // DELETE: api/dish/{id}
        [HttpDelete("{id}")]
        [HasPermission(Permissions.Menu)]
        public async Task<IActionResult> Delete(int id)
        {
            var dish = await _context.Dishes
                .Include(s => s.Children)
                .Include(s => s.Images)
                .Include(s => s.Addons)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (dish == null)
                return NotFound(new { Message = $"Шаурма с ID {id} не найдена" });

            _context.Dishes.Remove(dish);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Шаурма успешно удалена" });
        }

        // PUT: api/dish/reorder
        [HttpPut("reorder")]
        [HasPermission(Permissions.Menu)]
        public async Task<IActionResult> Reorder([FromBody] List<ReorderItem> items)
        {
            foreach (var item in items)
            {
                var dish = await _context.Dishes.FindAsync(item.Id);
                if (dish != null)
                    dish.SortOrder = item.SortOrder;
            }

            await _context.SaveChangesAsync();
            return Ok(new { Message = "Порядок обновлён" });
        }
    }

    public class CreateDishDto
    {
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string? Description { get; set; }
        public bool IsSpicy { get; set; }
        public bool IsAvailable { get; set; } = true;
        public bool IsPromo { get; set; }
        public int? ParentId { get; set; }
        public int SortOrder { get; set; }
        public string? Category { get; set; }
        public string? Badge { get; set; }
        /// <summary>Где можно заказать: «Доставка», «Самовывоз», «В зале»; пусто/все — везде</summary>
        public List<string>? AvailableOrderTypes { get; set; }
        /// <summary>Кухня, куда идёт блюдо; null или 0 — как у карточки / по умолчанию</summary>
        public int? KitchenStationId { get; set; }
    }

    public class UpdateDishDto
    {
        public string? Name { get; set; }
        public decimal? Price { get; set; }
        public string? Description { get; set; }
        public bool? IsSpicy { get; set; }
        public bool? IsAvailable { get; set; }
        public bool? IsPromo { get; set; }
        public int? ParentId { get; set; }
        public int? SortOrder { get; set; }
        public string? Category { get; set; }
        public string? Badge { get; set; }
        /// <summary>Где можно заказать: «Доставка», «Самовывоз», «В зале»; пусто/все — везде</summary>
        public List<string>? AvailableOrderTypes { get; set; }
        /// <summary>Кухня, куда идёт блюдо; 0 — снять метку</summary>
        public int? KitchenStationId { get; set; }
    }

    public class ReorderItem
    {
        public int Id { get; set; }
        public int SortOrder { get; set; }
    }
}