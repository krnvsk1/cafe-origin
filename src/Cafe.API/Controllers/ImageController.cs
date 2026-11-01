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
    public class ImageController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ImageController> _logger;

        public ImageController(
            ApplicationDbContext context, 
            IWebHostEnvironment environment,
            ILogger<ImageController> logger)
        {
            _context = context;
            _environment = environment;
            _logger = logger;
        }

        [HttpPost("upload/{dishId}")]
        [HasPermission(Permissions.Menu)]
        public async Task<IActionResult> UploadImage(int dishId, IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return BadRequest(new { Message = "Файл не выбран" });

                // Валидация размера файла (максимум 5MB)
                const long maxFileSize = 5 * 1024 * 1024;
                if (file.Length > maxFileSize)
                    return BadRequest(new { Message = "Размер файла не должен превышать 5 МБ" });

                // Валидация типа файла
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
                var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(fileExtension))
                    return BadRequest(new { Message = "Допустимые форматы: .jpg, .jpeg, .png, .webp, .gif" });

                // Проверяем, существует ли блюдо
                var dish = await _context.Dishes.FindAsync(dishId);
                if (dish == null)
                    return NotFound(new { Message = "Блюдо не найдено" });

                // Создаём папку для изображений, если её нет
                var uploadsFolder = Path.Combine(_environment.WebRootPath ?? "wwwroot", "uploads");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                // Генерируем уникальное имя файла
                var fileName = $"{Guid.NewGuid()}{fileExtension}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                // Сохраняем файл
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // Сохраняем информацию в БД
                var image = new DishImage
                {
                    DishId = dishId,
                    FileName = fileName,
                    FilePath = $"/uploads/{fileName}",
                    IsPrimary = !await _context.DishImages.AnyAsync(i => i.DishId == dishId),
                    CreatedAt = DateTime.UtcNow
                };

                await _context.DishImages.AddAsync(image);
                await _context.SaveChangesAsync();

                return Ok(new { 
                    Message = "Изображение загружено", 
                    ImagePath = image.FilePath,
                    IsPrimary = image.IsPrimary 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при загрузке изображения");
                return StatusCode(500, new { Message = "Ошибка при загрузке изображения" });
            }
        }

        [HttpGet("dish/{dishId}")]
        public async Task<IActionResult> GetImages(int dishId)
        {
            try
            {
                var images = await _context.DishImages
                    .Where(i => i.DishId == dishId)
                    .OrderByDescending(i => i.IsPrimary)
                    .ThenByDescending(i => i.CreatedAt)
                    .Select(i => new
                    {
                        i.Id,
                        i.FilePath,
                        i.IsPrimary,
                        i.CreatedAt
                    })
                    .ToListAsync();

                return Ok(images);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении изображений");
                return StatusCode(500, new { Message = "Ошибка при получении изображений" });
            }
        }

        [HttpDelete("{id}")]
        [HasPermission(Permissions.Menu)]
        public async Task<IActionResult> DeleteImage(int id)
        {
            try
            {
                var image = await _context.DishImages.FindAsync(id);
                if (image == null)
                    return NotFound(new { Message = "Изображение не найдено" });

                // Удаляем файл
                var filePath = Path.Combine(_environment.WebRootPath ?? "wwwroot", image.FilePath.TrimStart('/'));
                if (System.IO.File.Exists(filePath))
                    System.IO.File.Delete(filePath);

                // Удаляем запись из БД
                _context.DishImages.Remove(image);
                await _context.SaveChangesAsync();

                return Ok(new { Message = "Изображение удалено" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении изображения");
                return StatusCode(500, new { Message = "Ошибка при удалении изображения" });
            }
        }
    }
}