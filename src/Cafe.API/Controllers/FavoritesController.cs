using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Cafe.API.Data;
using Cafe.API.Extensions;
using Cafe.API.Models;

namespace Cafe.API.Controllers
{
    /// <summary>
    /// Избранные позиции меню пользователя
    /// </summary>
    [ApiController]
    [Route("api/favorites")]
    [Authorize]
    public class FavoritesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public FavoritesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/favorites — список ID избранных позиций
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            var ids = await _context.UserFavorites
                .Where(f => f.UserId == userId.Value)
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => f.DishId)
                .ToListAsync();

            return Ok(ids);
        }

        // POST: api/favorites/{dishId}
        [HttpPost("{dishId}")]
        public async Task<IActionResult> Add(int dishId)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            if (!await _context.Dishes.AnyAsync(s => s.Id == dishId))
                return NotFound(new { Message = "Позиция не найдена" });

            var exists = await _context.UserFavorites
                .AnyAsync(f => f.UserId == userId.Value && f.DishId == dishId);
            if (!exists)
            {
                _context.UserFavorites.Add(new UserFavorite
                {
                    UserId = userId.Value,
                    DishId = dishId,
                    CreatedAt = DateTime.UtcNow
                });
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // Параллельный запрос уже добавил — это не ошибка
                }
            }

            return Ok(new { DishId = dishId, IsFavorite = true });
        }

        // DELETE: api/favorites/{dishId}
        [HttpDelete("{dishId}")]
        public async Task<IActionResult> Remove(int dishId)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            await _context.UserFavorites
                .Where(f => f.UserId == userId.Value && f.DishId == dishId)
                .ExecuteDeleteAsync();

            return Ok(new { DishId = dishId, IsFavorite = false });
        }
    }
}
