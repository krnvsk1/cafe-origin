using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Kumashaurma.API.Options;

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

        public SettingsController(IOptions<RestaurantOptions> restaurant)
        {
            _restaurant = restaurant.Value;
        }

        [HttpGet]
        public IActionResult Get()
        {
            var nowUtc = DateTime.UtcNow;
            return Ok(new
            {
                _restaurant.TimeZone,
                _restaurant.OpenTime,
                _restaurant.CloseTime,
                _restaurant.MinLeadMinutes,
                _restaurant.MaxDaysAhead,
                IsOpenNow = _restaurant.IsWithinWorkingHours(nowUtc),
                ServerTimeUtc = nowUtc
            });
        }
    }
}
