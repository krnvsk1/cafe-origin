using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Kumashaurma.API.Services;
using Kumashaurma.API.Authorization;

namespace Kumashaurma.API.Controllers
{
    /// <summary>
    /// Импорт меню с сайта kumashaurma.ru (только для администратора)
    /// </summary>
    [ApiController]
    [Route("api/admin/menu-import")]
    [HasPermission(Permissions.MenuImport)]
    public class MenuImportController : ControllerBase
    {
        private readonly MenuImportService _import;
        private readonly ILogger<MenuImportController> _logger;

        public MenuImportController(MenuImportService import, ILogger<MenuImportController> logger)
        {
            _import = import;
            _logger = logger;
        }

        public class ImportRequest
        {
            /// <summary>Страница меню; по умолчанию https://kumashaurma.ru/</summary>
            public string? SourceUrl { get; set; }
            /// <summary>Скачать фото к себе (иначе — ссылки на сайт)</summary>
            public bool DownloadImages { get; set; } = true;
        }

        [HttpPost]
        public async Task<IActionResult> Import([FromBody] ImportRequest? request, CancellationToken ct)
        {
            var url = request?.SourceUrl;
            if (!string.IsNullOrWhiteSpace(url))
            {
                // Разрешаем только сайт заведения, чтобы эндпоинт нельзя было использовать для запросов куда угодно
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
                    || !(uri.Host == "kumashaurma.ru" || uri.Host.EndsWith(".kumashaurma.ru")))
                    return BadRequest(new { Message = "Можно импортировать только с https://kumashaurma.ru" });
            }

            try
            {
                var result = await _import.ImportAsync(url, request?.DownloadImages ?? true, ct);
                return Ok(result);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Импорт меню: сайт недоступен");
                return StatusCode(502, new { Message = "Не удалось открыть kumashaurma.ru — проверьте интернет и попробуйте ещё раз" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }
    }
}
