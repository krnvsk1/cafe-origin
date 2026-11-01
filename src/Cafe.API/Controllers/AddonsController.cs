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
    public class AddonsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AddonsController> _logger;

        public AddonsController(ApplicationDbContext context, ILogger<AddonsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: api/addons/categories
        [HttpGet("categories")]
        public async Task<IActionResult> GetAllCategories()
        {
            try
            {
                var categories = await _context.AddonCategories
                    .Include(c => c.Addons)
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.Name)
                    .ToListAsync();

                return Ok(categories);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении категорий добавок");
                return StatusCode(500, new { Message = "Ошибка при получении категорий" });
            }
        }

        // GET: api/addons/categories/{id}
        [HttpGet("categories/{id}")]
        public async Task<IActionResult> GetCategoryById(int id)
        {
            try
            {
                var category = await _context.AddonCategories
                    .Include(c => c.Addons)
                    .FirstOrDefaultAsync(c => c.Id == id && c.IsActive);

                if (category == null)
                    return NotFound(new { Message = $"Категория с ID {id} не найдена" });

                return Ok(category);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении категории {CategoryId}", id);
                return StatusCode(500, new { Message = "Ошибка при получении категории" });
            }
        }

        // POST: api/addons/categories
        [HttpPost("categories")]
        [HasPermission(Permissions.Menu)]
        public async Task<IActionResult> CreateCategory([FromBody] AddonCategory category)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(category.Name))
                    return BadRequest(new { Message = "Название категории обязательно" });

                category.CreatedAt = DateTime.UtcNow;
                category.IsActive = true;

                _context.AddonCategories.Add(category);
                await _context.SaveChangesAsync();

                _logger.LogInformation("✅ Создана категория добавок: {CategoryName} (ID: {CategoryId})", 
                    category.Name, category.Id);

                return CreatedAtAction(nameof(GetCategoryById), new { id = category.Id }, category);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании категории");
                return StatusCode(500, new { Message = "Ошибка при создании категории" });
            }
        }

        // PUT: api/addons/categories/{id}
        [HttpPut("categories/{id}")]
        [HasPermission(Permissions.Menu)]
        public async Task<IActionResult> UpdateCategory(int id, [FromBody] AddonCategory updatedCategory)
        {
            try
            {
                var category = await _context.AddonCategories.FindAsync(id);
                if (category == null)
                    return NotFound(new { Message = $"Категория с ID {id} не найдена" });

                category.Name = updatedCategory.Name ?? category.Name;
                category.Description = updatedCategory.Description ?? category.Description;
                category.IsRequired = updatedCategory.IsRequired;
                category.MinSelections = updatedCategory.MinSelections;
                category.MaxSelections = updatedCategory.MaxSelections;
                category.DisplayOrder = updatedCategory.DisplayOrder;
                category.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation("🔄 Обновлена категория добавок: {CategoryName} (ID: {CategoryId})", 
                    category.Name, category.Id);

                return Ok(category);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении категории {CategoryId}", id);
                return StatusCode(500, new { Message = "Ошибка при обновлении категории" });
            }
        }

        // DELETE: api/addons/categories/{id}
        [HttpDelete("categories/{id}")]
        [HasPermission(Permissions.Menu)]
        public async Task<IActionResult> DeleteCategory(int id)
        {
            try
            {
                var category = await _context.AddonCategories.FindAsync(id);
                if (category == null)
                    return NotFound(new { Message = $"Категория с ID {id} не найдена" });

                // Soft delete - просто деактивируем
                category.IsActive = false;
                category.UpdatedAt = DateTime.UtcNow;
                
                await _context.SaveChangesAsync();

                _logger.LogInformation("🗑️ Деактивирована категория добавок: {CategoryName} (ID: {CategoryId})", 
                    category.Name, category.Id);

                return Ok(new { Message = "Категория удалена" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении категории {CategoryId}", id);
                return StatusCode(500, new { Message = "Ошибка при удалении категории" });
            }
        }

        // ==================== ADDONS ====================

        // GET: api/addons
        [HttpGet]
        public async Task<IActionResult> GetAllAddons()
        {
            try
            {
                var addons = await _context.Addons
                    .Include(a => a.Category)
                    .Where(a => a.IsAvailable)
                    .OrderBy(a => a.Name)
                    .ToListAsync();

                return Ok(addons);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении добавок");
                return StatusCode(500, new { Message = "Ошибка при получении добавок" });
            }
        }

        // GET: api/addons/dish/{dishId}
        [HttpGet("dish/{dishId}")]
        public async Task<IActionResult> GetAddonsForDish(int dishId)
        {
            try
            {
                var dish = await _context.Dishes
                    .Include(s => s.Addons)
                        .ThenInclude(sa => sa.Addon)
                        .ThenInclude(a => a.Category)
                    .Include(s => s.Parent)
                        .ThenInclude(p => p!.Addons)
                            .ThenInclude(sa => sa.Addon)
                                .ThenInclude(a => a.Category)
                    .FirstOrDefaultAsync(s => s.Id == dishId);

                if (dish == null)
                    return NotFound(new { Message = "Блюдо не найдено" });

                // Если у блюда нет своих добавок и оно — вариант карточки, берём добавки карточки
                var addonsSource = dish.Addons;
                if (!dish.Addons.Any() && dish.Parent != null)
                {
                    addonsSource = dish.Parent.Addons;
                }

                var categories = addonsSource
                    .Where(sa => sa.Addon.IsAvailable && sa.Addon.Category.IsActive)
                    .GroupBy(sa => sa.Addon.Category)
                    .Select(g => new
                    {
                        Id = g.Key.Id,
                        Name = g.Key.Name,
                        Description = g.Key.Description,
                        IsRequired = g.Key.IsRequired,
                        MinSelections = g.Key.MinSelections,
                        MaxSelections = g.Key.MaxSelections,
                        DisplayOrder = g.Key.DisplayOrder,
                        Addons = g
                            .OrderBy(sa => sa.Addon.DisplayOrder).ThenBy(sa => sa.Addon.Name)
                            .Select(sa => new
                            {
                                Id = sa.Addon.Id,
                                AddonCategoryId = sa.Addon.AddonCategoryId,
                                Name = sa.Addon.Name,
                                Description = sa.Addon.Description,
                                Price = sa.CustomPrice ?? sa.Addon.Price,
                                IsAvailable = sa.Addon.IsAvailable,
                                MaxQuantity = sa.MaxQuantity,
                                IsDefault = sa.IsDefault
                            }).ToList()
                    })
                    // Сначала обязательные группы (размер теста и т.п.), затем по порядку
                    .OrderByDescending(c => c.IsRequired).ThenBy(c => c.DisplayOrder).ThenBy(c => c.Name)
                    .ToList();

                return Ok(categories);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении добавок для блюда {DishId}", dishId);
                return StatusCode(500, new { Message = "Ошибка при получении добавок" });
            }
        }

        // POST: api/addons
        public class CreateAddonDto
        {
            public string Name { get; set; } = string.Empty;
            public string? Description { get; set; }
            public decimal Price { get; set; }
            public int AddonCategoryId { get; set; }
            public bool IsAvailable { get; set; } = true;
            public int DisplayOrder { get; set; }
        }

        [HttpPost]
        [HasPermission(Permissions.Menu)]
        public async Task<IActionResult> CreateAddon([FromBody] CreateAddonDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Name))
                    return BadRequest(new { Message = "Название добавки обязательно" });

                var category = await _context.AddonCategories.FindAsync(dto.AddonCategoryId);
                if (category == null)
                    return BadRequest(new { Message = "Категория не найдена" });

                var addon = new Addon
                {
                    Name = dto.Name,
                    Description = dto.Description,
                    Price = dto.Price,
                    AddonCategoryId = dto.AddonCategoryId,
                    IsAvailable = dto.IsAvailable,
                    DisplayOrder = dto.DisplayOrder,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Addons.Add(addon);
                await _context.SaveChangesAsync();

                _logger.LogInformation("✅ Создана добавка: {AddonName} (ID: {AddonId})", addon.Name, addon.Id);

                return Ok(addon);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании добавки");
                return StatusCode(500, new { Message = "Ошибка при создании добавки" });
            }
        }

        // PUT: api/addons/{id}
        [HttpPut("{id}")]
        [HasPermission(Permissions.Menu)]
        public async Task<IActionResult> UpdateAddon(int id, [FromBody] Addon updatedAddon)
        {
            try
            {
                var addon = await _context.Addons.FindAsync(id);
                if (addon == null)
                    return NotFound(new { Message = $"Добавка с ID {id} не найдена" });

                addon.Name = updatedAddon.Name ?? addon.Name;
                addon.Description = updatedAddon.Description ?? addon.Description;
                addon.Price = updatedAddon.Price;
                addon.DisplayOrder = updatedAddon.DisplayOrder;
                addon.IsAvailable = updatedAddon.IsAvailable;
                addon.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation("🔄 Обновлена добавка: {AddonName} (ID: {AddonId})", addon.Name, addon.Id);

                return Ok(addon);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении добавки {AddonId}", id);
                return StatusCode(500, new { Message = "Ошибка при обновлении добавки" });
            }
        }

        // DELETE: api/addons/{id}
        [HttpDelete("{id}")]
        [HasPermission(Permissions.Menu)]
        public async Task<IActionResult> DeleteAddon(int id)
        {
            try
            {
                var addon = await _context.Addons.FindAsync(id);
                if (addon == null)
                    return NotFound(new { Message = $"Добавка с ID {id} не найдена" });

                _context.Addons.Remove(addon);
                await _context.SaveChangesAsync();

                _logger.LogInformation("🗑️ Удалена добавка: {AddonName} (ID: {AddonId})", addon.Name, addon.Id);

                return Ok(new { Message = "Добавка удалена" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении добавки {AddonId}", id);
                return StatusCode(500, new { Message = "Ошибка при удалении добавки" });
            }
        }

        // ==================== DISH-ADDON LINKS ====================

        public class LinkAddonRequest
        {
            [System.Text.Json.Serialization.JsonPropertyName("dishId")]
            public int DishId { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("addonId")]
            public int AddonId { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("customPrice")]
            public decimal? CustomPrice { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("isDefault")]
            public bool IsDefault { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("maxQuantity")]
            public int MaxQuantity { get; set; } = 1;
        }

        // POST: api/addons/link-to-dish
        [HttpPost("link-to-dish")]
        [HasPermission(Permissions.Menu)]
        public async Task<IActionResult> LinkAddonToDish([FromBody] LinkAddonRequest request)
        {
            try
            {
                var dish = await _context.Dishes
                    .Include(s => s.Children)
                    .FirstOrDefaultAsync(s => s.Id == request.DishId);
                if (dish == null)
                    return NotFound(new { Message = "Блюдо не найдено" });

                var addon = await _context.Addons.FindAsync(request.AddonId);
                if (addon == null)
                    return NotFound(new { Message = "Добавка не найдена" });

                var existing = await _context.DishAddons
                    .FirstOrDefaultAsync(sa => sa.DishId == request.DishId && sa.AddonId == request.AddonId);

                if (existing != null)
                    return BadRequest(new { Message = "Эта добавка уже привязана к товару" });

                var dishAddon = new DishAddon
                {
                    DishId = request.DishId,
                    AddonId = request.AddonId,
                    CustomPrice = request.CustomPrice,
                    IsDefault = request.IsDefault,
                    MaxQuantity = request.MaxQuantity
                };

                _context.DishAddons.Add(dishAddon);

                // Если привязываем к карточке-родителю — дублируем на всех дочерних
                int duplicatedCount = 0;
                if (dish.ParentId == null && dish.Children.Any())
                {
                    foreach (var child in dish.Children)
                    {
                        var childExisting = await _context.DishAddons
                            .FirstOrDefaultAsync(sa => sa.DishId == child.Id && sa.AddonId == request.AddonId);

                        if (childExisting == null)
                        {
                            _context.DishAddons.Add(new DishAddon
                            {
                                DishId = child.Id,
                                AddonId = request.AddonId,
                                CustomPrice = request.CustomPrice,
                                IsDefault = request.IsDefault,
                                MaxQuantity = request.MaxQuantity
                            });
                            duplicatedCount++;
                        }
                    }
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("🔗 Добавка {AddonName} привязана к товару {DishName}{Duplicated}",
                    addon.Name, dish.Name,
                    duplicatedCount > 0 ? $" (+ дублирована на {duplicatedCount} дочерних)" : "");

                return Ok(new
                {
                    Message = duplicatedCount > 0
                        ? $"Добавка привязана к карточке и дублирована на {duplicatedCount} дочерних"
                        : "Добавка привязана к товару",
                    DishAddonId = dishAddon.Id,
                    DuplicatedCount = duplicatedCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при привязке добавки к товару");
                return StatusCode(500, new { Message = "Ошибка при привязке добавки" });
            }
        }

        // DELETE: api/addons/unlink-from-dish
        [HttpDelete("unlink-from-dish")]
        [HasPermission(Permissions.Menu)]
        public async Task<IActionResult> UnlinkAddonFromDish(int dishId, int addonId)
        {
            try
            {
                var linksToRemove = await _context.DishAddons
                    .Where(sa => sa.DishId == dishId && sa.AddonId == addonId)
                    .ToListAsync();

                if (!linksToRemove.Any())
                    return NotFound(new { Message = "Связь не найдена" });

                // Если отвязываем от карточки-родителя — отвязать и от всех дочерних
                var dish = await _context.Dishes
                    .Include(s => s.Children)
                    .FirstOrDefaultAsync(s => s.Id == dishId);

                if (dish != null && dish.ParentId == null && dish.Children.Any())
                {
                    foreach (var child in dish.Children)
                    {
                        var childLinks = await _context.DishAddons
                            .Where(sa => sa.DishId == child.Id && sa.AddonId == addonId)
                            .ToListAsync();

                        _context.DishAddons.RemoveRange(childLinks);
                    }
                }

                _context.DishAddons.RemoveRange(linksToRemove);
                await _context.SaveChangesAsync();

                _logger.LogInformation("🔓 Добавка {AddonId} отвязана от товара {DishId} (+ дочерних)", addonId, dishId);

                return Ok(new { Message = "Добавка отвязана от товара" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отвязке добавки от товара");
                return StatusCode(500, new { Message = "Ошибка при отвязке добавки" });
            }
        }
    }
}