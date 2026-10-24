using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Kumashaurma.API.Data;
using Kumashaurma.API.Extensions;
using Kumashaurma.API.Models;

namespace Kumashaurma.API.Controllers
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
                .Select(f => f.ShawarmaId)
                .ToListAsync();

            return Ok(ids);
        }

        // POST: api/favorites/{shawarmaId}
        [HttpPost("{shawarmaId}")]
        public async Task<IActionResult> Add(int shawarmaId)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            if (!await _context.Shawarmas.AnyAsync(s => s.Id == shawarmaId))
                return NotFound(new { Message = "Позиция не найдена" });

            var exists = await _context.UserFavorites
                .AnyAsync(f => f.UserId == userId.Value && f.ShawarmaId == shawarmaId);
            if (!exists)
            {
                _context.UserFavorites.Add(new UserFavorite
                {
                    UserId = userId.Value,
                    ShawarmaId = shawarmaId,
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

            return Ok(new { ShawarmaId = shawarmaId, IsFavorite = true });
        }

        // DELETE: api/favorites/{shawarmaId}
        [HttpDelete("{shawarmaId}")]
        public async Task<IActionResult> Remove(int shawarmaId)
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized();

            await _context.UserFavorites
                .Where(f => f.UserId == userId.Value && f.ShawarmaId == shawarmaId)
                .ExecuteDeleteAsync();

            return Ok(new { ShawarmaId = shawarmaId, IsFavorite = false });
        }
    }
}
