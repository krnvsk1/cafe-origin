using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Cafe.API.Services;
using Cafe.API.Authorization;

namespace Cafe.API.Controllers
{
    /// <summary>
    /// Импорт меню с сайта заведения на платформе FoodPicasso (только для администратора)
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
            /// <summary>Страница меню на сайте заведения (https)</summary>
            public string? SourceUrl { get; set; }
            /// <summary>Скачать фото к себе (иначе — ссылки на сайт)</summary>
            public bool DownloadImages { get; set; } = true;
        }

        [HttpPost]
        public async Task<IActionResult> Import([FromBody] ImportRequest? request, CancellationToken ct)
        {
            var url = request?.SourceUrl?.Trim();
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                return BadRequest(new { Message = "Укажите адрес страницы меню, начиная с https://" });
            // Только сайты в интернете: адреса внутренней сети сервер открывать не должен
            if (!await IsPublicHostAsync(uri.Host, ct))
                return BadRequest(new { Message = "Этот адрес недоступен для импорта" });

            try
            {
                var result = await _import.ImportAsync(url, request?.DownloadImages ?? true, ct);
                return Ok(result);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Импорт меню: сайт недоступен");
                return StatusCode(502, new { Message = "Не удалось открыть сайт — проверьте адрес и интернет и попробуйте ещё раз" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        private static async Task<bool> IsPublicHostAsync(string host, CancellationToken ct)
        {
            try
            {
                var addresses = await System.Net.Dns.GetHostAddressesAsync(host, ct);
                return addresses.Length > 0 && addresses.All(IsPublic);
            }
            catch (System.Net.Sockets.SocketException) { return false; }
        }

        private static bool IsPublic(System.Net.IPAddress ip)
        {
            if (System.Net.IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal) return false;
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            var b = ip.GetAddressBytes();
            if (b.Length != 4) return true;
            return !(b[0] == 10 || b[0] == 127 || b[0] == 0 || (b[0] == 169 && b[1] == 254)
                     || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127));
        }
    }
}
