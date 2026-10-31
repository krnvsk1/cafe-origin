using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Kumashaurma.API.Authorization;
using Kumashaurma.API.Extensions;
using Kumashaurma.API.Options;
using Kumashaurma.API.Services;

namespace Kumashaurma.API.Controllers
{
    /// <summary>
    /// Публичные настройки заведения, нужные клиенту (часы работы и т.п.)
    /// </summary>
    [ApiController]
    [Route("api/settings")]
    public class SettingsController : ControllerBase
    {
        private readonly RestaurantOptions _restaurant;
        private readonly IPaymentService _payments;
        private readonly BrandSettingsProvider _brand;

        public SettingsController(IOptions<RestaurantOptions> restaurant, IPaymentService payments, BrandSettingsProvider brand)
        {
            _restaurant = restaurant.Value;
            _payments = payments;
            _brand = brand;
        }

        public class BrandRequest
        {
            public string Name { get; set; } = string.Empty;
            public string? Tagline { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var nowUtc = DateTime.UtcNow;
            var brand = await _brand.GetAsync();
            return Ok(new
            {
                BrandName = brand.DisplayName,
                BrandTagline = brand.Tagline,
                _restaurant.TimeZone,
                _restaurant.OpenTime,
                _restaurant.CloseTime,
                _restaurant.MinLeadMinutes,
                _restaurant.MaxDaysAhead,
                _restaurant.Latitude,
                _restaurant.Longitude,
                IsOpenNow = _restaurant.IsWithinWorkingHours(nowUtc),
                OnlinePaymentEnabled = _payments.OnlineEnabled,
                ServerTimeUtc = nowUtc
            });
        }

        // PUT: api/settings/brand — название заведения и подзаголовок
        [HttpPut("brand")]
        [HasPermission(Permissions.Settings)]
        public async Task<IActionResult> SaveBrand([FromBody] BrandRequest r)
        {
            var name = (r.Name ?? "").Trim();
            if (name.Length == 0) return BadRequest(new { message = "Введите название заведения" });
            if (name.Length > 60) return BadRequest(new { message = "Название — не длиннее 60 символов" });
            var tagline = string.IsNullOrWhiteSpace(r.Tagline) ? null : r.Tagline.Trim();
            if (tagline?.Length > 200) return BadRequest(new { message = "Подзаголовок — не длиннее 200 символов" });
            await _brand.SaveAsync(new BrandOptions { Name = name, Tagline = tagline }, User.GetUserId());
            return await Get();
        }
    }
}
